using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Domain.Entities;

namespace TitleClaimTracker.Infrastructure.Data;

public sealed class TitleClaimDbContext(DbContextOptions<TitleClaimDbContext> options) : DbContext(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<FilingType> FilingTypes => Set<FilingType>();
    public DbSet<LegalFiling> LegalFilings => Set<LegalFiling>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Property>(e => { e.HasKey(x => x.PropertyID); e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()"); e.HasMany(x => x.LegalFilings).WithOne(x => x.Property).HasForeignKey(x => x.PropertyID).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<FilingType>(e => { e.HasKey(x => x.FilingTypeID); e.HasIndex(x => x.TypeName).IsUnique(); e.HasMany(x => x.LegalFilings).WithOne(x => x.FilingType).HasForeignKey(x => x.FilingTypeID).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<LegalFiling>(e => { e.HasKey(x => x.FilingID); e.Property(x => x.RiskScore).HasColumnType("float"); e.HasCheckConstraint("CK_LegalFilings_Status", "Status IN ('New','Under Review','Escalated','Resolved','Closed')"); e.HasCheckConstraint("CK_LegalFilings_RiskScore", "RiskScore IS NULL OR (RiskScore >= 0 AND RiskScore <= 1)"); });
        modelBuilder.Entity<AuditLog>(e => { e.HasKey(x => x.AuditID); e.Property(x => x.ChangeDate).HasDefaultValueSql("SYSUTCDATETIME()"); });
        modelBuilder.Entity<FilingType>().HasData(
            new FilingType { FilingTypeID = 1, TypeName = "Title Claim" },
            new FilingType { FilingTypeID = 2, TypeName = "Lien" },
            new FilingType { FilingTypeID = 3, TypeName = "Easement" },
            new FilingType { FilingTypeID = 4, TypeName = "Deed Dispute" });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var changes = ChangeTracker.Entries<LegalFiling>().Where(x => x.State == EntityState.Modified).SelectMany(entry => entry.Properties.Where(p => p.IsModified && p.Metadata.Name is "Status" or "ClaimantName" or "RiskScore").Select(p => new AuditLog { TableName = "LegalFilings", RecordID = entry.Entity.FilingID, ColumnChanged = p.Metadata.Name, OldValue = p.OriginalValue?.ToString(), NewValue = p.CurrentValue?.ToString(), ChangeDate = DateTime.UtcNow })).ToList();
        if (changes.Count > 0) AuditLogs.AddRange(changes);
        return await base.SaveChangesAsync(cancellationToken);
    }
}
