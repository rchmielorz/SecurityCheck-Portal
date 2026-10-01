using System.ComponentModel.DataAnnotations;

namespace securitycheck_portal.Core.Scanning;

/// <summary>Scan worker and Trivy settings, bound from <c>Scan</c>.</summary>
public sealed class ScanOptions
{
    public const string SectionName = "Scan";

    /// <summary><c>trivy</c> from PATH by default; on the server set an absolute path to the pinned binary.</summary>
    [Required]
    public string TrivyExecutablePath { get; set; } = "trivy";

    /// <summary>Fixed folder for the Trivy vulnerability DB, writable only by the worker account.</summary>
    [Required(ErrorMessage = "Scan:CacheDirectory is required (e.g. D:\\trivy-cache).")]
    public string CacheDirectory { get; set; } = "";

    /// <summary>Short folder for checkouts (<c>WorkRoot/scanId</c>); Windows MAX_PATH is a concern.</summary>
    [Required(ErrorMessage = "Scan:WorkRoot is required (e.g. D:\\scw).")]
    public string WorkRoot { get; set; } = "";

    /// <summary>Limit of the <c>trivy fs</c> run.</summary>
    [Range(1, 120)]
    public int ScanTimeoutMinutes { get; set; } = 15;

    /// <summary>Overall limit of <c>git clone</c> (not the short <c>Git:TimeoutSeconds</c>).</summary>
    [Range(1, 120)]
    public int CloneTimeoutMinutes { get; set; } = 10;

    /// <summary>Limit of the vulnerability DB download.</summary>
    [Range(1, 120)]
    public int DbUpdateTimeoutMinutes { get; set; } = 5;

    /// <summary>A scan fails when the DB (after an update attempt) is older than this.</summary>
    [Range(1, 90)]
    public int MaxDbAgeDays { get; set; } = 7;

    /// <summary>Optional mirror of the Trivy DB (OCI repository), passed as <c>--db-repository</c>.</summary>
    public string? DbRepository { get; set; }

    /// <summary>When set, any other Trivy version is refused (the pinned, verified release).</summary>
    public string? ExpectedTrivyVersion { get; set; }

    /// <summary>How often the worker looks for a queued scan.</summary>
    [Range(1, 3600)]
    public int PollIntervalSeconds { get; set; } = 5;
}
