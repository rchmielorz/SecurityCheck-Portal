using System.Text.Json;
using securitycheck_portal.Core.Data;

namespace securitycheck_portal.Core.Scanning;

/// <summary>A parsed <c>trivy fs --format json</c> report.</summary>
/// <param name="TargetCount">Number of <c>Results[]</c> entries. Zero means Trivy found no dependency file (not "clean").</param>
/// <param name="Findings">Merged: one row per (library, version, vulnerability) with all files in <c>Targets</c>.</param>
public sealed record TrivyReport(int TargetCount, IReadOnlyList<ScanFinding> Findings);

public static class TrivyReportParser
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
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Empty(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Cut(value, max);

    private static string Cut(string value, int max) => value.Length > max ? value[..max] : value;
}
