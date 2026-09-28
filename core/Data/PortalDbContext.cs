using Microsoft.EntityFrameworkCore;

namespace securitycheck_portal.Core.Data;

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    private const int EnumTextMaxLength = 32;

    public DbSet<Repository> Repositories => Set<Repository>();

    public DbSet<VersionPattern> VersionPatterns => Set<VersionPattern>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

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
    }
}
