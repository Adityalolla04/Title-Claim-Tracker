// File: TitleClaimTracker/Core/DTOs/FilingDtos.cs
using System.ComponentModel.DataAnnotations;

namespace TitleClaimTracker.Core.DTOs;

public sealed record CreateClaimDto(
    [property: Required, MaxLength(255)] string Address,
    [property: Required, MaxLength(100)] string City,
    [property: Required, StringLength(2)] string State,
    string? LegalDescription,
    [property: Required, MaxLength(100)] string FilingType,
    DateTime DateFiled,
    [property: MaxLength(255)] string? ClaimantName,
    string? Notes,
    [property: Range(0, 1)] double? RiskScore);

public sealed record UpdateClaimDto(
    [property: Required, MaxLength(255)] string Address,
    [property: Required, MaxLength(100)] string City,
    [property: Required, StringLength(2)] string State,
    string? LegalDescription,
    [property: Required, MaxLength(100)] string FilingType,
    DateTime DateFiled,
    [property: MaxLength(255)] string? ClaimantName,
    string? Notes,
    [property: Required] string RowVersion);

public sealed record UpdateClaimStatusDto(
    [property: Required, MaxLength(50)] string Status,
    [property: Required] string RowVersion,
    [property: MaxLength(500)] string? Reason = null);

public sealed record ClaimSummaryDto(
    int FilingID, int PropertyID, string Address, string City, string State,
    string FilingType, DateTime DateFiled, string Status, string? ClaimantName,
    string? Notes, double? RiskScore);

public sealed record ClaimStatusHistoryDto(long StatusHistoryID, string? FromStatus, string ToStatus, DateTime TransitionedUtc, string? Reason);

public sealed record ClaimDetailDto(
    int FilingID, int PropertyID, string Address, string City, string State,
    string? LegalDescription, string FilingType, DateTime DateFiled, string Status,
    string? ClaimantName, string? Notes, double? RiskScore, string CreationSource,
    decimal? ClassificationScore, byte TriagePriority, string RowVersion,
    IReadOnlyList<ClaimStatusHistoryDto> StatusTimeline);

public sealed record ChatTriageRequestDto([property: Required, MinLength(10)] string Message);

public sealed record SubmitTriageDraftDto(
    int TriageAttemptID,
    [property: Required, MaxLength(255)] string Address,
    [property: Required, MaxLength(100)] string City,
    [property: Required, StringLength(2)] string State,
    string? LegalDescription,
    [property: Required, MaxLength(100)] string FilingType,
    [property: MaxLength(255)] string? ClaimantName,
    string? Notes);

public sealed record TriageAttemptDto(long TriageAttemptID, string Outcome, string UiOutcome, string? PredictedFilingType, decimal? ClassificationScore, byte? HeuristicPriority, string? ExtractedAddress, string? ExtractedCity, string? ExtractedState, string? ExtractedClaimantName, string? ExtractedNotes, int? CreatedFilingID, DateTime CreatedUtc);

public sealed record TriageFeedbackDto(long TriageAttemptID, bool WasCorrect, int? CorrectedFilingTypeID, string? CorrectedIssueTags, string? CorrectionNotes);

public sealed record ClaimExtractionDto(
    string? ClaimantName, string FilingType, double RiskScore, string LegalNotes, double ConfidenceScore);

public sealed record ChatbotResponseDto(
    string Message, bool RequiresConfirmation, ClaimExtractionDto? ExtractedClaim, ClaimSummaryDto? SavedFiling,
    long? TriageAttemptID = null, string Outcome = "needsInformation", string ExtractionMode = "deterministic");

public sealed record PagedClaimsDto(IReadOnlyList<ClaimSummaryDto> Items, int Total, int Page, int PageSize);

public sealed record RiskReportDto(int PropertyID, string Address, string City, string State, double AverageRiskScore, int OpenClaimsCount, int FilingCount, int FilingTypeCount);
