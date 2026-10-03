using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using securitycheck_portal.Core.Data;

namespace securitycheck_portal.Core.Scanning;

/// <summary>A parsed <c>trivy fs --format json</c> report.</summary>
/// <param name="TargetCount">Number of <c>Results[]</c> entries. Zero means Trivy found no dependency file (not "clean").</param>
/// <param name="Findings">Merged: one row per (library, version, vulnerability) with all files in <c>Targets</c>.</param>
public sealed record TrivyReport(int TargetCount, IReadOnlyList<ScanFinding> Findings);

public static partial class TrivyReportParser
{
    /// <returns><c>false</c> for anything that is not a Trivy report (invalid JSON, wrong shape, a vulnerability without its key fields).</returns>
    public static bool TryParse(string json, out TrivyReport? report)
    {
        report = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            report = Read(document.RootElement);
            return report is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TrivyReport? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var targetCount = 0;
        var merged = new Dictionary<(string Library, string Version, string Id), ScanFinding>();

        if (root.TryGetProperty("Results", out var results) && results.ValueKind != JsonValueKind.Null)
        {
            if (results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var result in results.EnumerateArray())
            {
                if (result.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                targetCount++;
                var target = Text(result, "Target") ?? "";

                if (!result.TryGetProperty("Vulnerabilities", out var vulnerabilities)
                    || vulnerabilities.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (vulnerabilities.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var vulnerability in vulnerabilities.EnumerateArray())
                {
                    if (!Add(merged, vulnerability, target))
                    {
                        return null;
                    }
                }
            }
        }

        return new TrivyReport(targetCount, [.. merged.Values]);
    }

    private static bool Add(
        Dictionary<(string Library, string Version, string Id), ScanFinding> merged, JsonElement vulnerability, string target)
    {
        if (vulnerability.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Dropping a row we cannot identify would under-report, so such a report is rejected as a whole.
        var id = Text(vulnerability, "VulnerabilityID");
        var library = Text(vulnerability, "PkgName");
        var version = Text(vulnerability, "InstalledVersion");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        id = Cut(id, ScanFinding.VulnerabilityIdMaxLength);
        library = Cut(library, ScanFinding.LibraryMaxLength);
        version = Cut(version, ScanFinding.VersionMaxLength);
        var severity = ParseSeverity(Text(vulnerability, "Severity"));

        if (merged.TryGetValue((library, version, id), out var existing))
        {
            if (target.Length > 0 && !existing.Targets.Contains(target, StringComparer.Ordinal))
            {
                existing.Targets = [.. existing.Targets, target];
            }

            // Enum order is Critical..Unknown, so the lower value is the more serious known one.
            if (severity < existing.Severity)
            {
                existing.Severity = severity;
            }

            existing.FixedVersion ??= Empty(Text(vulnerability, "FixedVersion"), ScanFinding.VersionMaxLength);
            existing.Title ??= Empty(Text(vulnerability, "Title"), ScanFinding.TitleMaxLength);
            return true;
        }

        merged[(library, version, id)] = new ScanFinding
        {
            Library = library,
            InstalledVersion = version,
            VulnerabilityId = id,
            Severity = severity,
            FixedVersion = Empty(Text(vulnerability, "FixedVersion"), ScanFinding.VersionMaxLength),
            Title = Empty(Text(vulnerability, "Title"), ScanFinding.TitleMaxLength),
            Targets = target.Length > 0 ? [target] : [],
        };
        return true;
    }

    /// <summary>Anything Trivy sends that is not one of the four known levels (including UNKNOWN) is <see cref="FindingSeverity.Unknown"/>.</summary>
    public static FindingSeverity ParseSeverity(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "CRITICAL" => FindingSeverity.Critical,
        "HIGH" => FindingSeverity.High,
        "MEDIUM" => FindingSeverity.Medium,
        "LOW" => FindingSeverity.Low,
        _ => FindingSeverity.Unknown,
    };

    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return Clean(value.GetString());
        }
        catch (InvalidOperationException)
        {
            // GetString refuses an escaped lone surrogate ("\ud800"); replace those escapes and read again.
            var raw = value.GetRawText();
            var replaced = EscapeSequence().Replace(raw[1..^1], match =>
                match.Length == 6 && IsSurrogateEscape(match.Value) ? "\\uFFFD" : match.Value);
            return Clean(JsonSerializer.Deserialize<string>("\"" + replaced + "\""));
        }
    }

    // One JSON escape: a valid surrogate pair (12 chars), a single \uXXXX, or any other backslash pair (so \\u is not misread).
    [GeneratedRegex(@"\\u[dD][89abAB][0-9a-fA-F]{2}\\u[dD][c-fC-F][0-9a-fA-F]{2}|\\u[0-9a-fA-F]{4}|\\.")]
    private static partial Regex EscapeSequence();

    private static bool IsSurrogateEscape(string escape)
        => escape[1] == 'u' && char.IsSurrogate((char)Convert.ToInt32(escape[2..], 16));

    private static string? Empty(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Cut(value, max);

    /// <summary>
    /// PostgreSQL rejects U+0000 and invalid UTF-16 in text columns, which would lose the whole scan result:
    /// NUL is dropped and a lone surrogate becomes U+FFFD.
    /// </summary>
    internal static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            char? replacement = null;
            var drop = false;
            if (c == '\0')
            {
                drop = true;
            }
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder?.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }
            else if (char.IsSurrogate(c))
            {
                replacement = '�';
            }

            if (!drop && replacement is null)
            {
                builder?.Append(c);
                continue;
            }

            if (builder is null)
            {
                builder = new StringBuilder(value.Length);
                builder.Append(value, 0, i);
            }

            if (replacement is { } r)
            {
                builder.Append(r);
            }
        }

        return builder?.ToString() ?? value;
    }

    /// <summary>Cuts to at most <paramref name="max"/> chars, never between the two halves of a surrogate pair.</summary>
    private static string Cut(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[max - 1]) && char.IsLowSurrogate(value[max]) ? max - 1 : max;
        return value[..length];
    }
}
