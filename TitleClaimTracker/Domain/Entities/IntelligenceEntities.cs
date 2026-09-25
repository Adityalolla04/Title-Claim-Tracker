using TitleClaimTracker.Domain.Identity;

namespace TitleClaimTracker.Domain.Entities;

public sealed class ClaimStatusHistory
{
    public long StatusHistoryID { get; set; }
    public int FilingID { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public DateTime TransitionedUtc { get; set; }
    public string? Reason { get; set; }

    public LegalFiling? Filing { get; set; }
    public ApplicationUser? ActorUser { get; set; }
}

public sealed class TriageAttempt
{
    public long TriageAttemptID { get; set; }
    public string SubmittedByUserId { get; set; } = string.Empty;
    public string InputText { get; set; } = string.Empty;
    public int? PredictedFilingTypeID { get; set; }
    public decimal? ClassificationScore { get; set; }
    public byte? HeuristicPriority { get; set; }
    public string TriageRuleVersion { get; set; } = string.Empty;
    public long? ModelVersionID { get; set; }
    public int? CreatedFilingID { get; set; }
    public string Outcome { get; set; } = "AwaitingConfirmation";
    public DateTime CreatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? ExtractedClaimantName { get; set; }
    public string? ExtractedAddress { get; set; }
    public string? ExtractedCity { get; set; }
    public string? ExtractedState { get; set; }
    public string? ExtractedNotes { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public ApplicationUser? SubmittedByUser { get; set; }
    public FilingType? PredictedFilingType { get; set; }
    public ModelVersion? ModelVersion { get; set; }
    public LegalFiling? CreatedFiling { get; set; }
    public TriageFeedback? Feedback { get; set; }
}

public sealed class TriageFeedback
{
    public long TriageFeedbackID { get; set; }
    public long TriageAttemptID { get; set; }
    public string ReviewedByUserId { get; set; } = string.Empty;
    public bool WasCorrect { get; set; }
    public int? CorrectedFilingTypeID { get; set; }
    public string? CorrectedIssueTags { get; set; }
    public string? CorrectionNotes { get; set; }
    public DateTime ReviewedUtc { get; set; }

    public TriageAttempt? TriageAttempt { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
    public FilingType? CorrectedFilingType { get; set; }
}

public sealed class ModelVersion
{
    public long ModelVersionID { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? ArtifactUri { get; set; }
    public byte[]? ArtifactSha256 { get; set; }
    public string DatasetProvenance { get; set; } = string.Empty;
    public DateTime? TrainedUtc { get; set; }
    public DateTime RegisteredUtc { get; set; }
    public bool IsActive { get; set; }

    public ICollection<LegalFiling> Filings { get; set; } = new List<LegalFiling>();
    public ICollection<TriageAttempt> TriageAttempts { get; set; } = new List<TriageAttempt>();
    public ICollection<ModelEvaluationResult> EvaluationResults { get; set; } = new List<ModelEvaluationResult>();
}

public sealed class ModelEvaluationResult
{
    public long ModelEvaluationResultID { get; set; }
    public long ModelVersionID { get; set; }
    public string DatasetName { get; set; } = string.Empty;
    public string DatasetVersion { get; set; } = string.Empty;
    public string DatasetProvenance { get; set; } = string.Empty;
    public DateTime EvaluatedUtc { get; set; }
    public long SampleCount { get; set; }
    public decimal Accuracy { get; set; }
    public decimal MacroF1 { get; set; }
    public decimal WeightedF1 { get; set; }
    public decimal? PrecisionScore { get; set; }
    public decimal? RecallScore { get; set; }
    public string? Notes { get; set; }

    public ModelVersion? ModelVersion { get; set; }
}

public sealed class IdempotencyRecord
{
    public long IdempotencyRecordID { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public byte[] PayloadHash { get; set; } = Array.Empty<byte>();
    public string State { get; set; } = "Started";
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public ApplicationUser? User { get; set; }
}