using Microsoft.EntityFrameworkCore;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Core.Data;

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    private const int EnumTextMaxLength = 32;

    public DbSet<Repository> Repositories => Set<Repository>();

    public DbSet<VersionPattern> VersionPatterns => Set<VersionPattern>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Scan> Scans => Set<Scan>();

    public DbSet<ScanFinding> ScanFindings => Set<ScanFinding>();

    public DbSet<ScanUnscannedItem> ScanUnscannedItems => Set<ScanUnscannedItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Repository>(repository =>
        {
            repository.Property(r => r.Url).HasMaxLength(Repository.UrlMaxLength);
            repository.Property(r => r.Name).HasMaxLength(Repository.NameMaxLength);
            repository.Property(r => r.CreatedBy).HasMaxLength(AuditEvent.ActorMaxLength);
            repository.HasIndex(r => r.Url).IsUnique();
        });

        modelBuilder.Entity<VersionPattern>(pattern =>
        {
            pattern.Property(p => p.Pattern).HasMaxLength(VersionPattern.PatternMaxLength);
            pattern.Property(p => p.CreatedBy).HasMaxLength(AuditEvent.ActorMaxLength);
            pattern.Property(p => p.LastResolutionState)
                .HasConversion<string>()
                .HasMaxLength(EnumTextMaxLength);
            pattern.Property(p => p.LastResolvedTag).HasMaxLength(VersionPattern.TagMaxLength);
            pattern.Property(p => p.LastResolvedCommit)
                .HasMaxLength(VersionPattern.CommitLength)
                .IsFixedLength();

            // Restrict: a repository can be deleted only after all its patterns are gone.
            pattern.HasOne(p => p.Repository)
                .WithMany(r => r.Patterns)
                .HasForeignKey(p => p.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Covers inactive patterns too: re-adding a deactivated pattern is a conflict, not a new row.
            pattern.HasIndex(p => new { p.RepositoryId, p.Pattern }).IsUnique();
        });

        modelBuilder.Entity<AuditEvent>(auditEvent =>
        {
            auditEvent.Property(e => e.Actor).HasMaxLength(AuditEvent.ActorMaxLength);
            auditEvent.Property(e => e.Action)
                .HasConversion<string>()
                .HasMaxLength(EnumTextMaxLength);
            auditEvent.Property(e => e.RepositoryUrl).HasMaxLength(Repository.UrlMaxLength);
            auditEvent.Property(e => e.Pattern).HasMaxLength(VersionPattern.PatternMaxLength);
            auditEvent.HasIndex(e => new { e.RepositoryId, e.OccurredAt });
        });

        // PatternId and RepositoryId are plain columns (no FK): scan history outlives the pattern and repository.
        modelBuilder.Entity<Scan>(scan =>
        {
            scan.Property(s => s.RepositoryUrl).HasMaxLength(Repository.UrlMaxLength);
            scan.Property(s => s.Pattern).HasMaxLength(VersionPattern.PatternMaxLength);
            scan.Property(s => s.Status)
                .HasConversion<string>()
                .HasMaxLength(EnumTextMaxLength);
            scan.Property(s => s.FailureReason)
                .HasConversion<string>()
                .HasMaxLength(EnumTextMaxLength);
            scan.Property(s => s.FailureDetail).HasMaxLength(Scan.FailureDetailMaxLength);
            scan.Property(s => s.RequestedBy).HasMaxLength(AuditEvent.ActorMaxLength);
            scan.Property(s => s.ScannedTag).HasMaxLength(VersionPattern.TagMaxLength);
            scan.Property(s => s.ScannedCommit)
                .HasMaxLength(VersionPattern.CommitLength)
                .IsFixedLength();
            scan.Property(s => s.TrivyVersion).HasMaxLength(Scan.TrivyVersionMaxLength);

            // At most one active (queued or running) scan per pattern.
            scan.HasIndex(s => s.PatternId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Queued','Running')");
            scan.HasIndex(s => new { s.PatternId, s.RequestedAt });
        });

        modelBuilder.Entity<ScanFinding>(finding =>
        {
            finding.Property(f => f.Library).HasMaxLength(ScanFinding.LibraryMaxLength);
            finding.Property(f => f.InstalledVersion).HasMaxLength(ScanFinding.VersionMaxLength);
            finding.Property(f => f.VulnerabilityId).HasMaxLength(ScanFinding.VulnerabilityIdMaxLength);
            finding.Property(f => f.Severity)
                .HasConversion<string>()
                .HasMaxLength(EnumTextMaxLength);
            finding.Property(f => f.FixedVersion).HasMaxLength(ScanFinding.VersionMaxLength);
            finding.Property(f => f.Title).HasMaxLength(ScanFinding.TitleMaxLength);

            finding.HasOne(f => f.Scan)
                .WithMany(s => s.Findings)
                .HasForeignKey(f => f.ScanId)
                .OnDelete(DeleteBehavior.Cascade);

            finding.HasIndex(f => new { f.ScanId, f.Library, f.InstalledVersion, f.VulnerabilityId })
                .IsUnique();
        });

        modelBuilder.Entity<ScanUnscannedItem>(item =>
        {
            item.Property(i => i.Path).HasMaxLength(UnscannedItem.PathMaxLength);
            item.Property(i => i.Reason)
                .HasConversion<string>()
                .HasMaxLength(EnumTextMaxLength);
            item.Property(i => i.Detail).HasMaxLength(UnscannedItem.DetailMaxLength);

            item.HasOne(i => i.Scan)
                .WithMany(s => s.UnscannedItems)
                .HasForeignKey(i => i.ScanId)
                .OnDelete(DeleteBehavior.Cascade);

            item.HasIndex(i => new { i.ScanId, i.Path }).IsUnique();
        });
    }
}
