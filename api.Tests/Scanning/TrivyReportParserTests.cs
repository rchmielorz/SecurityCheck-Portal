using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

public sealed class TrivyReportParserTests
{
    private const string WithVulnerabilities = """
        {
          "SchemaVersion": 2,
          "ArtifactName": "scan-1",
          "Results": [
            {
              "Target": "packages.lock.json",
              "Class": "lang-pkgs",
              "Type": "nuget",
              "Vulnerabilities": [
                {
                  "VulnerabilityID": "CVE-2024-0001",
                  "PkgName": "Newtonsoft.Json",
                  "InstalledVersion": "12.0.1",
                  "FixedVersion": "13.0.1",
                  "Severity": "HIGH",
                  "Title": "Insecure deserialization"
                },
                {
                  "VulnerabilityID": "CVE-2024-0002",
                  "PkgName": "System.Text.Encodings.Web",
                  "InstalledVersion": "4.5.0",
                  "Severity": "CRITICAL"
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Report_with_vulnerabilities_is_read_field_by_field()
    {
        Assert.True(TrivyReportParser.TryParse(WithVulnerabilities, out var report));

        Assert.Equal(1, report!.TargetCount);
        Assert.Equal(2, report.Findings.Count);
        var high = report.Findings.Single(f => f.VulnerabilityId == "CVE-2024-0001");
        Assert.Equal("Newtonsoft.Json", high.Library);
        Assert.Equal("12.0.1", high.InstalledVersion);
        Assert.Equal("13.0.1", high.FixedVersion);
        Assert.Equal(FindingSeverity.High, high.Severity);
        Assert.Equal("Insecure deserialization", high.Title);
        Assert.Equal(["packages.lock.json"], high.Targets);

        var critical = report.Findings.Single(f => f.VulnerabilityId == "CVE-2024-0002");
        Assert.Equal(FindingSeverity.Critical, critical.Severity);
        Assert.Null(critical.FixedVersion);
        Assert.Null(critical.Title);
    }

    [Fact]
    public void Targets_without_vulnerabilities_are_a_clean_report_with_targets()
    {
        const string Clean = """
            {"Results":[{"Target":"packages.lock.json","Class":"lang-pkgs","Type":"nuget"},
                        {"Target":"web/package-lock.json","Class":"lang-pkgs","Type":"npm","Vulnerabilities":null}]}
            """;

        Assert.True(TrivyReportParser.TryParse(Clean, out var report));

        Assert.Equal(2, report!.TargetCount);
        Assert.Empty(report.Findings);
    }

    [Theory]
    [InlineData("""{"SchemaVersion":2,"ArtifactName":"scan-1"}""")]
    [InlineData("""{"SchemaVersion":2,"Results":null}""")]
    [InlineData("""{"Results":[]}""")]
    public void Report_without_targets_has_zero_targets_so_it_is_not_clean(string json)
    {
        Assert.True(TrivyReportParser.TryParse(json, out var report));

        Assert.Equal(0, report!.TargetCount);
        Assert.Empty(report.Findings);
    }

    [Theory]
    [InlineData("UNKNOWN", FindingSeverity.Unknown)]
    [InlineData("SOMETHING-NEW", FindingSeverity.Unknown)]
    [InlineData("", FindingSeverity.Unknown)]
    [InlineData("low", FindingSeverity.Low)]
    [InlineData("MEDIUM", FindingSeverity.Medium)]
    public void Severity_that_is_not_recognised_is_unknown(string severity, FindingSeverity expected)
    {
        var json = $$"""
            {"Results":[{"Target":"a","Vulnerabilities":[
              {"VulnerabilityID":"CVE-1","PkgName":"lib","InstalledVersion":"1.0","Severity":"{{severity}}"}]}]}
            """;

        Assert.True(TrivyReportParser.TryParse(json, out var report));

        Assert.Equal(expected, report!.Findings.Single().Severity);
    }

    [Fact]
    public void Missing_severity_is_unknown()
    {
        const string Json = """
            {"Results":[{"Target":"a","Vulnerabilities":[
              {"VulnerabilityID":"CVE-1","PkgName":"lib","InstalledVersion":"1.0"}]}]}
            """;

        Assert.True(TrivyReportParser.TryParse(Json, out var report));

        Assert.Equal(FindingSeverity.Unknown, report!.Findings.Single().Severity);
    }

    [Fact]
    public void Same_library_version_and_cve_in_two_files_is_one_row_with_both_targets()
    {
        const string Json = """
            {"Results":[
              {"Target":"a/packages.lock.json","Vulnerabilities":[
                {"VulnerabilityID":"CVE-1","PkgName":"lib","InstalledVersion":"1.0","Severity":"HIGH","Title":"t"}]},
              {"Target":"b/packages.lock.json","Vulnerabilities":[
                {"VulnerabilityID":"CVE-1","PkgName":"lib","InstalledVersion":"1.0","Severity":"HIGH","FixedVersion":"1.1"},
                {"VulnerabilityID":"CVE-1","PkgName":"lib","InstalledVersion":"2.0","Severity":"HIGH"}]}]}
            """;

        Assert.True(TrivyReportParser.TryParse(Json, out var report));

        Assert.Equal(2, report!.TargetCount);
        Assert.Equal(2, report.Findings.Count);
        var merged = report.Findings.Single(f => f.InstalledVersion == "1.0");
        Assert.Equal(["a/packages.lock.json", "b/packages.lock.json"], merged.Targets);
        Assert.Equal("1.1", merged.FixedVersion);
        Assert.Equal("t", merged.Title);
        Assert.Equal(["b/packages.lock.json"], report.Findings.Single(f => f.InstalledVersion == "2.0").Targets);
    }

    [Fact]
    public void Merged_row_keeps_the_most_serious_known_severity()
    {
        const string Json = """
            {"Results":[
              {"Target":"a","Vulnerabilities":[{"VulnerabilityID":"CVE-1","PkgName":"lib","InstalledVersion":"1","Severity":"UNKNOWN"}]},
              {"Target":"b","Vulnerabilities":[{"VulnerabilityID":"CVE-1","PkgName":"lib","InstalledVersion":"1","Severity":"MEDIUM"}]}]}
            """;

        Assert.True(TrivyReportParser.TryParse(Json, out var report));

        Assert.Equal(FindingSeverity.Medium, report!.Findings.Single().Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Results\":[")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"Results":"oops"}""")]
    [InlineData("""{"Results":[{"Target":"a","Vulnerabilities":[{"PkgName":"lib","InstalledVersion":"1"}]}]}""")]
    public void Invalid_json_or_shape_is_rejected(string json)
    {
        Assert.False(TrivyReportParser.TryParse(json, out var report));
        Assert.Null(report);
    }
}
