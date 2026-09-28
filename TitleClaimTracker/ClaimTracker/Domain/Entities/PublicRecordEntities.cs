namespace TitleClaimTracker.Domain.Entities;

public sealed class DataSource
{
    public int DataSourceID { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string ProvenanceStatement { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ICollection<IngestionRun> IngestionRuns { get; set; } = new List<IngestionRun>();
    public ICollection<ExternalProperty> ExternalProperties { get; set; } = new List<ExternalProperty>();
    public ICollection<ExternalPropertyDocument> ExternalDocuments { get; set; } = new List<ExternalPropertyDocument>();
}

public sealed class IngestionRun
{
    public long IngestionRunID { get; set; }
    public int DataSourceID { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? LastCheckpoint { get; set; }
    public long RowsRead { get; set; }
    public long RowsAccepted { get; set; }
    public long RowsUpdated { get; set; }
    public long RowsRejected { get; set; }
    public long ErrorCount { get; set; }
    public DateTime CreatedUtc { get; set; }

    public DataSource? DataSource { get; set; }
    public ICollection<IngestionQuarantineError> QuarantineErrors { get; set; } = new List<IngestionQuarantineError>();
    public ICollection<ExternalPropertyDocument> LastSeenDocuments { get; set; } = new List<ExternalPropertyDocument>();
}

public sealed class IngestionQuarantineError
{
    public long QuarantineErrorID { get; set; }
    public long IngestionRunID { get; set; }
    public string ExternalRecordKey { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? RawPayload { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }

    public IngestionRun? IngestionRun { get; set; }
}

public sealed class ExternalProperty
{
    public long ExternalPropertyID { get; set; }
    public int DataSourceID { get; set; }
    public string ExternalPropertyKey { get; set; } = string.Empty;
    public string? ParcelIdentifier { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? County { get; set; }
    public string? RawPayload { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public DataSource? DataSource { get; set; }
    public ICollection<ExternalDocumentProperty> DocumentRelationships { get; set; } = new List<ExternalDocumentProperty>();
}

public sealed class ExternalPropertyDocument
{
    public long ExternalPropertyDocumentID { get; set; }
    public int DataSourceID { get; set; }
    public string ExternalDocumentKey { get; set; } = string.Empty;
    public string SourceDocumentType { get; set; } = string.Empty;
    public DateTime? RecordedDate { get; set; }
    public string? DocumentUrl { get; set; }
    public string? RawPayload { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public long? LastIngestionRunID { get; set; }

    public DataSource? DataSource { get; set; }
    public IngestionRun? LastIngestionRun { get; set; }
    public ICollection<ExternalDocumentProperty> PropertyRelationships { get; set; } = new List<ExternalDocumentProperty>();
}

public sealed class ExternalDocumentProperty
{
    public long ExternalPropertyDocumentID { get; set; }
    public long ExternalPropertyID { get; set; }
    public string RelationshipType { get; set; } = string.Empty;
    public string? SourceRelationshipKey { get; set; }

    public ExternalPropertyDocument? Document { get; set; }
    public ExternalProperty? Property { get; set; }
}
