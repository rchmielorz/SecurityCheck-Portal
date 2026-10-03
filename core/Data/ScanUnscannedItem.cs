using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Core.Data;

/// <summary>A dependency manifest of a scan that was not scanned, with the reason.</summary>
public sealed class ScanUnscannedItem
{
    public long Id { get; set; }

    public long ScanId { get; set; }

    public Scan? Scan { get; set; }

    /// <summary>Relative path with forward slashes, e.g. <c>src/App/packages.lock.json</c>.</summary>
    public required string Path { get; set; }

    public UnscannedReason Reason { get; set; }

    /// <summary>Short, human-readable detail; never contains secrets.</summary>
    public string? Detail { get; set; }
}
