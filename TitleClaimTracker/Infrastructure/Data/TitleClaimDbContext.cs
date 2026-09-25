using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Domain.Identity;
using TitleClaimTracker.Infrastructure.Security;

namespace TitleClaimTracker.Infrastructure.Data;

public sealed class TitleClaimDbContext(DbContextOptions<TitleClaimDbContext> options, ICurrentUserAccessor? currentUser = null)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<FilingType> FilingTypes => Set<FilingType>();
    public DbSet<LegalFiling> LegalFilings => Set<LegalFiling>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ClaimStatusHistory> ClaimStatusHistory => Set<ClaimStatusHistory>();
    public DbSet<TriageAttempt> TriageAttempts => Set<TriageAttempt>();
    public DbSet<TriageFeedback> TriageFeedback => Set<TriageFeedback>();
    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();
    public DbSet<ModelEvaluationResult> ModelEvaluationResults => Set<ModelEvaluationResult>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<IngestionQuarantineError> IngestionQuarantineErrors => Set<IngestionQuarantineError>();
    public DbSet<ExternalProperty> ExternalProperties => Set<ExternalProperty>();
    public DbSet<ExternalPropertyDocument> ExternalPropertyDocuments => Set<ExternalPropertyDocument>();
    public DbSet<ExternalDocumentProperty> ExternalDocumentProperties => Set<ExternalDocumentProperty>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(e =>
        {
            e.ToTable("AspNetUsers");
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.CreatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });
        modelBuilder.Entity<IdentityRole>(e => e.ToTable("AspNetRoles"));
        modelBuilder.Entity<IdentityUserClaim<string>>(e => e.ToTable("AspNetUserClaims"));
        modelBuilder.Entity<IdentityUserLogin<string>>(e =>
        {
            e.ToTable("AspNetUserLogins", table => table.HasCheckConstraint("CK_AspNetUserLogins_KeyBytes", "DATALENGTH([LoginProvider]) + DATALENGTH([ProviderKey]) <= 1700"));
            e.HasKey(x => new { x.LoginProvider, x.ProviderKey }).IsClustered(false);
        });
        modelBuilder.Entity<IdentityUserRole<string>>(e =>
        {
            e.ToTable("AspNetUserRoles", table => table.HasCheckConstraint("CK_AspNetUserRoles_KeyBytes", "DATALENGTH([UserId]) + DATALENGTH([RoleId]) <= 1700"));
            e.HasKey(x => new { x.UserId, x.RoleId }).IsClustered(false);
        });
        modelBuilder.Entity<IdentityUserToken<string>>(e =>
        {
            e.ToTable("AspNetUserTokens", table => table.HasCheckConstraint("CK_AspNetUserTokens_KeyBytes", "DATALENGTH([UserId]) + DATALENGTH([LoginProvider]) + DATALENGTH([Name]) <= 1700"));
            e.HasKey(x => new { x.UserId, x.LoginProvider, x.Name }).IsClustered(false);
        });

        modelBuilder.Entity<Property>(e =>
        {
            e.ToTable("Properties");
            e.HasKey(x => x.PropertyID);
            e.Property(x => x.Address).HasMaxLength(255).IsRequired();
            e.Property(x => x.City).HasMaxLength(100).IsRequired();
            e.Property(x => x.State).HasColumnType("char(2)").IsRequired();
            e.Property(x => x.CreatedDate).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasMany(x => x.LegalFilings).WithOne(x => x.Property).HasForeignKey(x => x.PropertyID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FilingType>(e =>
        {
            e.ToTable("FilingTypes");
            e.HasKey(x => x.FilingTypeID);
            e.Property(x => x.TypeName).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.TypeName).IsUnique().HasDatabaseName("UQ_FilingTypes_TypeName");
            e.HasMany(x => x.LegalFilings).WithOne(x => x.FilingType).HasForeignKey(x => x.FilingTypeID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LegalFiling>(e =>
        {
            e.ToTable("LegalFilings", table =>
            {
                table.HasCheckConstraint("CK_LegalFilings_Status", "Status IN ('New','Under Review','Escalated','Resolved','Closed','Pending Review','Active Investigation')");
                table.HasCheckConstraint("CK_LegalFilings_RiskScore", "RiskScore IS NULL OR (RiskScore >= 0 AND RiskScore <= 1)");
                table.HasCheckConstraint("CK_LegalFilings_ClassificationScore", "ClassificationScore IS NULL OR (ClassificationScore >= 0 AND ClassificationScore <= 1)");
                table.HasCheckConstraint("CK_LegalFilings_CreationSource", "CreationSource IN ('Manual','Assisted')");
                table.HasCheckConstraint("CK_LegalFilings_TriagePriority", "TriagePriority BETWEEN 1 AND 5");
                table.HasCheckConstraint("CK_LegalFilings_SoftDelete", "(IsDeleted = 0 AND DeletedUtc IS NULL AND DeletedByUserId IS NULL) OR IsDeleted = 1");
                table.HasCheckConstraint("CK_LegalFilings_IssueTagsJson", "IssueTags IS NULL OR ISJSON(IssueTags) = 1");
            });
            e.HasKey(x => x.FilingID);
            e.Property(x => x.DateFiled).HasColumnType("date");
            e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("New").IsRequired();
            e.Property(x => x.ClaimantName).HasMaxLength(255);
            e.Property(x => x.RiskScore).HasColumnType("float");
            e.Property(x => x.SubmittedByUserId).HasMaxLength(450);
            e.Property(x => x.CreatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.UpdatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.CreationSource).HasMaxLength(20).HasDefaultValue("Manual").IsRequired();
            e.Property(x => x.ClassificationScore).HasPrecision(9, 8);
            e.Property(x => x.ModelVersionID);
            e.Property(x => x.TriagePriority).HasDefaultValue((byte)3).IsRequired();
            e.Property(x => x.TriageRuleVersion).HasMaxLength(50).HasDefaultValue("triage-v1").IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();
            e.Property(x => x.IsSyntheticDemoData).HasDefaultValue(false).IsRequired();
            e.HasIndex(x => new { x.SubmittedByUserId, x.Status, x.CreatedUtc })
                .HasDatabaseName("IX_LegalFilings_SubmittedByUser_Status_CreatedUtc")
                .HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.Status, x.CreatedUtc })
                .HasDatabaseName("IX_LegalFilings_Status_CreatedUtc")
                .HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.PropertyID, x.Status }).HasDatabaseName("IX_LegalFilings_Property_Status");
            e.HasIndex(x => new { x.FilingTypeID, x.DateFiled }).HasDatabaseName("IX_LegalFilings_Type_Date");
            e.HasOne(x => x.SubmittedByUser).WithMany(x => x.SubmittedFilings).HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeletedByUser).WithMany(x => x.DeletedFilings).HasForeignKey(x => x.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ModelVersion).WithMany(x => x.Filings).HasForeignKey(x => x.ModelVersionID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLog", table => table.HasCheckConstraint("CK_AuditLog_Action", "Action IN ('Insert','Update','SoftDelete','StatusTransition')"));
            e.HasKey(x => x.AuditID);
            e.Property(x => x.TableName).HasMaxLength(128).IsRequired();
            e.Property(x => x.ColumnChanged).HasMaxLength(100);
            e.Property(x => x.ChangeDate).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ActorUserId).HasMaxLength(450);
            e.Property(x => x.Action).HasMaxLength(30).HasDefaultValue("Update").IsRequired();
            e.HasIndex(x => new { x.TableName, x.RecordID, x.ChangeDate }).HasDatabaseName("IX_AuditLog_Record");
            e.HasOne(x => x.ActorUser).WithMany(x => x.AuditEntries).HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClaimStatusHistory>(e =>
        {
            e.ToTable("ClaimStatusHistory", table => table.HasCheckConstraint(
                "CK_ClaimStatusHistory_Transition",
                "(FromStatus IS NULL AND ToStatus IN ('New','Under Review','Escalated','Resolved','Closed','Pending Review','Active Investigation')) OR " +
                "(FromStatus = 'New' AND ToStatus IN ('Under Review','Escalated','Closed')) OR " +
                "(FromStatus = 'Under Review' AND ToStatus IN ('Escalated','Resolved','Closed')) OR " +
                "(FromStatus = 'Escalated' AND ToStatus IN ('Under Review','Resolved','Closed')) OR " +
                "(FromStatus = 'Resolved' AND ToStatus = 'Closed') OR " +
                "(FromStatus IN ('Pending Review','Active Investigation') AND ToStatus IN ('Under Review','Escalated','Resolved','Closed'))"));
            e.HasKey(x => x.StatusHistoryID);
            e.Property(x => x.FromStatus).HasMaxLength(50);
            e.Property(x => x.ToStatus).HasMaxLength(50).IsRequired();
            e.Property(x => x.ActorUserId).HasMaxLength(450);
            e.Property(x => x.TransitionedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.FilingID, x.TransitionedUtc, x.StatusHistoryID }).HasDatabaseName("IX_ClaimStatusHistory_Filing_Time");
            e.HasIndex(x => new { x.ActorUserId, x.TransitionedUtc }).HasDatabaseName("IX_ClaimStatusHistory_Actor_Time");
            e.HasOne(x => x.Filing).WithMany(x => x.StatusHistory).HasForeignKey(x => x.FilingID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ActorUser).WithMany(x => x.StatusHistoryEntries).HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriageAttempt>(e =>
        {
            e.ToTable("TriageAttempts", table =>
            {
                table.HasCheckConstraint("CK_TriageAttempts_ClassificationScore", "ClassificationScore IS NULL OR (ClassificationScore >= 0 AND ClassificationScore <= 1)");
                table.HasCheckConstraint("CK_TriageAttempts_HeuristicPriority", "HeuristicPriority IS NULL OR HeuristicPriority BETWEEN 1 AND 5");
                table.HasCheckConstraint("CK_TriageAttempts_Outcome", "Outcome IN ('AwaitingConfirmation','Created','Rejected','Failed')");
            });
            e.HasKey(x => x.TriageAttemptID);
            e.Property(x => x.SubmittedByUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.ClassificationScore).HasPrecision(9, 8);
            e.Property(x => x.HeuristicPriority);
            e.Property(x => x.TriageRuleVersion).HasMaxLength(50).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(30).HasDefaultValue("AwaitingConfirmation").IsRequired();
            e.Property(x => x.CreatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ExtractedClaimantName).HasMaxLength(200);
            e.Property(x => x.ExtractedAddress).HasMaxLength(255);
            e.Property(x => x.ExtractedCity).HasMaxLength(100);
            e.Property(x => x.ExtractedState).HasColumnType("char(2)");
            e.Property(x => x.FailureCode).HasMaxLength(100);
            e.HasIndex(x => new { x.SubmittedByUserId, x.CreatedUtc }).HasDatabaseName("IX_TriageAttempts_User_CreatedUtc");
            e.HasOne(x => x.SubmittedByUser).WithMany(x => x.TriageAttempts).HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PredictedFilingType).WithMany().HasForeignKey(x => x.PredictedFilingTypeID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ModelVersion).WithMany(x => x.TriageAttempts).HasForeignKey(x => x.ModelVersionID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedFiling).WithMany(x => x.TriageAttempts).HasForeignKey(x => x.CreatedFilingID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriageFeedback>(e =>
        {
            e.ToTable("TriageFeedback", table => table.HasCheckConstraint("CK_TriageFeedback_Correction", "WasCorrect = 1 OR CorrectedFilingTypeID IS NOT NULL"));
            e.HasKey(x => x.TriageFeedbackID);
            e.Property(x => x.ReviewedByUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.CorrectedIssueTags);
            e.Property(x => x.ReviewedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.TriageAttemptID).IsUnique().HasDatabaseName("UQ_TriageFeedback_TriageAttempt");
            e.HasOne(x => x.TriageAttempt).WithOne(x => x.Feedback).HasForeignKey<TriageFeedback>(x => x.TriageAttemptID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReviewedByUser).WithMany(x => x.TriageFeedbackEntries).HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CorrectedFilingType).WithMany().HasForeignKey(x => x.CorrectedFilingTypeID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ModelVersion>(e =>
        {
            e.ToTable("ModelVersions");
            e.HasKey(x => x.ModelVersionID);
            e.Property(x => x.ModelName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Version).HasMaxLength(50).IsRequired();
            e.Property(x => x.ArtifactUri).HasMaxLength(2048);
            e.Property(x => x.ArtifactSha256).HasColumnType("binary(32)");
            e.Property(x => x.DatasetProvenance).IsRequired();
            e.Property(x => x.RegisteredUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.IsActive).HasDefaultValue(false).IsRequired();
            e.HasIndex(x => new { x.ModelName, x.Version }).IsUnique().HasDatabaseName("UQ_ModelVersions_Name_Version");
        });

        modelBuilder.Entity<ModelEvaluationResult>(e =>
        {
            e.ToTable("ModelEvaluationResults", table =>
            {
                table.HasCheckConstraint("CK_ModelEvaluationResults_SampleCount", "SampleCount >= 0");
                table.HasCheckConstraint("CK_ModelEvaluationResults_Scores", "Accuracy BETWEEN 0 AND 1 AND MacroF1 BETWEEN 0 AND 1 AND WeightedF1 BETWEEN 0 AND 1 AND (PrecisionScore IS NULL OR PrecisionScore BETWEEN 0 AND 1) AND (RecallScore IS NULL OR RecallScore BETWEEN 0 AND 1)");
            });
            e.HasKey(x => x.ModelEvaluationResultID);
            e.Property(x => x.DatasetName).HasMaxLength(200).IsRequired();
            e.Property(x => x.DatasetVersion).HasMaxLength(100).IsRequired();
            e.Property(x => x.DatasetProvenance).IsRequired();
            e.Property(x => x.EvaluatedUtc).HasColumnType("datetime2(7)");
            e.Property(x => x.Accuracy).HasPrecision(9, 8);
            e.Property(x => x.MacroF1).HasPrecision(9, 8);
            e.Property(x => x.WeightedF1).HasPrecision(9, 8);
            e.Property(x => x.PrecisionScore).HasPrecision(9, 8);
            e.Property(x => x.RecallScore).HasPrecision(9, 8);
            e.HasIndex(x => new { x.ModelVersionID, x.DatasetName, x.DatasetVersion }).IsUnique().HasDatabaseName("UQ_ModelEvaluationResults_Model_Dataset");
            e.HasOne(x => x.ModelVersion).WithMany(x => x.EvaluationResults).HasForeignKey(x => x.ModelVersionID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyRecords", table =>
            {
                table.HasCheckConstraint("CK_IdempotencyRecords_State", "State IN ('Started','Completed','Failed')");
                table.HasCheckConstraint("CK_IdempotencyRecords_PayloadHash", "DATALENGTH(PayloadHash) = 32");
            });
            e.HasKey(x => x.IdempotencyRecordID);
            e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.OperationName).HasMaxLength(100).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.PayloadHash).HasColumnType("varbinary(32)").IsRequired();
            e.Property(x => x.State).HasMaxLength(20).HasDefaultValue("Started").IsRequired();
            e.Property(x => x.CreatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.UserId, x.OperationName, x.IdempotencyKey }).IsUnique().HasDatabaseName("UQ_IdempotencyRecords_User_Operation_Key");
            e.HasOne(x => x.User).WithMany(x => x.IdempotencyRecords).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DataSource>(e =>
        {
            e.ToTable("DataSources");
            e.HasKey(x => x.DataSourceID);
            e.Property(x => x.SourceName).HasMaxLength(200).IsRequired();
            e.Property(x => x.SourceUrl).HasMaxLength(2048).IsRequired();
            e.Property(x => x.ProvenanceStatement).IsRequired();
            e.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
            e.Property(x => x.CreatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.UpdatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.SourceName).IsUnique().HasDatabaseName("UQ_DataSources_SourceName");
        });

        modelBuilder.Entity<IngestionRun>(e =>
        {
            e.ToTable("IngestionRuns", table =>
            {
                table.HasCheckConstraint("CK_IngestionRuns_Status", "Status IN ('Pending','Running','Completed','Failed','Cancelled')");
                table.HasCheckConstraint("CK_IngestionRuns_Counts", "RowsRead >= 0 AND RowsAccepted >= 0 AND RowsUpdated >= 0 AND RowsRejected >= 0 AND ErrorCount >= 0");
            });
            e.HasKey(x => x.IngestionRunID);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("Pending").IsRequired();
            e.Property(x => x.LastCheckpoint).HasMaxLength(500);
            e.Property(x => x.CreatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.DataSourceID, x.CreatedUtc }).HasDatabaseName("IX_IngestionRuns_Source_CreatedUtc");
            e.HasOne(x => x.DataSource).WithMany(x => x.IngestionRuns).HasForeignKey(x => x.DataSourceID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IngestionQuarantineError>(e =>
        {
            e.ToTable("IngestionQuarantineErrors");
            e.HasKey(x => x.QuarantineErrorID);
            e.Property(x => x.ExternalRecordKey).HasMaxLength(500).IsRequired();
            e.Property(x => x.ErrorCode).HasMaxLength(100).IsRequired();
            e.Property(x => x.ErrorMessage).IsRequired();
            e.Property(x => x.CreatedUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.IngestionRunID, x.ExternalRecordKey }).HasDatabaseName("IX_IngestionQuarantineErrors_Run_Record");
            e.HasOne(x => x.IngestionRun).WithMany(x => x.QuarantineErrors).HasForeignKey(x => x.IngestionRunID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExternalProperty>(e =>
        {
            e.ToTable("ExternalProperties");
            e.HasKey(x => x.ExternalPropertyID);
            e.Property(x => x.ExternalPropertyKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.ParcelIdentifier).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(255);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.State).HasColumnType("char(2)");
            e.Property(x => x.County).HasMaxLength(100);
            e.Property(x => x.FirstSeenUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.LastSeenUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.DataSourceID, x.ExternalPropertyKey }).IsUnique().HasDatabaseName("UQ_ExternalProperties_Source_Key");
            e.HasIndex(x => new { x.DataSourceID, x.ParcelIdentifier }).HasDatabaseName("IX_ExternalProperties_Source_Parcel");
            e.HasOne(x => x.DataSource).WithMany(x => x.ExternalProperties).HasForeignKey(x => x.DataSourceID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExternalPropertyDocument>(e =>
        {
            e.ToTable("ExternalPropertyDocuments");
            e.HasKey(x => x.ExternalPropertyDocumentID);
            e.Property(x => x.ExternalDocumentKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.SourceDocumentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.RecordedDate).HasColumnType("date");
            e.Property(x => x.DocumentUrl).HasMaxLength(2048);
            e.Property(x => x.FirstSeenUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.LastSeenUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.DataSourceID, x.ExternalDocumentKey }).IsUnique().HasDatabaseName("UQ_ExternalPropertyDocuments_Source_Key");
            e.HasIndex(x => x.SourceDocumentType).HasDatabaseName("IX_ExternalPropertyDocuments_DocumentType");
            e.HasOne(x => x.DataSource).WithMany(x => x.ExternalDocuments).HasForeignKey(x => x.DataSourceID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LastIngestionRun).WithMany(x => x.LastSeenDocuments).HasForeignKey(x => x.LastIngestionRunID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExternalDocumentProperty>(e =>
        {
            e.ToTable("ExternalDocumentProperties");
            e.HasKey(x => new { x.ExternalPropertyDocumentID, x.ExternalPropertyID });
            e.Property(x => x.RelationshipType).HasMaxLength(100).IsRequired();
            e.Property(x => x.SourceRelationshipKey).HasMaxLength(200);
            e.HasIndex(x => x.ExternalPropertyID).HasDatabaseName("IX_ExternalDocumentProperties_Property");
            e.HasOne(x => x.Document).WithMany(x => x.PropertyRelationships).HasForeignKey(x => x.ExternalPropertyDocumentID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Property).WithMany(x => x.DocumentRelationships).HasForeignKey(x => x.ExternalPropertyID).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var filingEntries = ChangeTracker.Entries<LegalFiling>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToList();
        foreach (var entry in filingEntries)
        {
            entry.Entity.UpdatedUtc = now;
        }

        var pendingAudits = new List<PendingAudit>();
        var pendingStatusHistory = new List<PendingStatusHistory>();
        foreach (var entry in ChangeTracker.Entries<LegalFiling>().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var action = entry.State == EntityState.Added
                ? "Insert"
                : entry.Entity.IsDeleted && entry.Property(x => x.IsDeleted).IsModified
                    ? "SoftDelete"
                    : entry.Property(x => x.Status).IsModified
                        ? "StatusTransition"
                        : "Update";

            if (entry.State == EntityState.Added)
            {
                pendingAudits.Add(new PendingAudit(entry.Entity, null, null, "created", action));
                pendingStatusHistory.Add(new PendingStatusHistory(entry.Entity, null, entry.Entity.Status, "Created"));
                continue;
            }

            foreach (var property in entry.Properties.Where(property => property.IsModified && AuditedColumns.Contains(property.Metadata.Name)))
            {
                if (Equals(property.OriginalValue, property.CurrentValue))
                {
                    continue;
                }

                pendingAudits.Add(new PendingAudit(
                    entry.Entity,
                    property.Metadata.Name,
                    ToAuditValue(property.OriginalValue),
                    ToAuditValue(property.CurrentValue),
                    action));
            }

            var statusProperty = entry.Property(x => x.Status);
            if (statusProperty.IsModified && !string.Equals(statusProperty.OriginalValue?.ToString(), statusProperty.CurrentValue?.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                pendingStatusHistory.Add(new PendingStatusHistory(
                    entry.Entity,
                    statusProperty.OriginalValue?.ToString(),
                    statusProperty.CurrentValue?.ToString() ?? string.Empty,
                    "Status update"));
            }
        }

        IDbContextTransaction? transaction = null;
        if (Database.CurrentTransaction is null)
        {
            transaction = await Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var changes = await base.SaveChangesAsync(cancellationToken);
            if (pendingAudits.Count > 0)
            {
                AuditLogs.AddRange(pendingAudits.Select(pending => new AuditLog
                {
                    TableName = "LegalFilings",
                    RecordID = pending.Filing.FilingID,
                    ColumnChanged = pending.ColumnChanged,
                    OldValue = pending.OldValue,
                    NewValue = pending.NewValue,
                    ChangeDate = now,
                    ActorUserId = currentUser?.UserId,
                    Action = pending.Action
                }));
            }

            if (pendingStatusHistory.Count > 0)
            {
                ClaimStatusHistory.AddRange(pendingStatusHistory.Select(pending => new ClaimStatusHistory
                {
                    FilingID = pending.Filing.FilingID,
                    FromStatus = pending.FromStatus,
                    ToStatus = pending.ToStatus,
                    ActorUserId = currentUser?.UserId,
                    TransitionedUtc = now,
                    Reason = pending.Reason
                }));
            }

            if (pendingAudits.Count > 0 || pendingStatusHistory.Count > 0)
            {
                changes += await base.SaveChangesAsync(cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return changes;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static readonly HashSet<string> AuditedColumns =
    ["Status", "ClaimantName", "Notes", "RiskScore", "ClassificationScore", "SubmittedByUserId", "CreationSource", "IssueTags", "TriagePriority", "IsDeleted", "DeletedUtc", "DeletedByUserId"];

    private static string? ToAuditValue(object? value) => value switch
    {
        null => null,
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O"),
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value.ToString()
    };

    private sealed record PendingAudit(LegalFiling Filing, string? ColumnChanged, string? OldValue, string? NewValue, string Action);

    private sealed record PendingStatusHistory(LegalFiling Filing, string? FromStatus, string ToStatus, string Reason);
}