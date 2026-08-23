using System.ComponentModel.DataAnnotations;

namespace TitleClaimTracker.Core.DTOs;

public sealed record CreateFilingDto(
    [property: Required, MaxLength(255)] string Address,
    [property: Required, MaxLength(100)] string City,
    [property: Required, StringLength(2)] string State,
    string? LegalDescription,
    [property: Required, MaxLength(50)] string FilingType,
    DateTime DateFiled,
    [property: MaxLength(200)] string? ClaimantName,
    string? Notes,
    [property: Range(0, 1)] double? RiskScore);

public sealed record UpdateFilingStatusDto([property: Required, MaxLength(50)] string Status);

public sealed record FilingSummaryDto(
    int FilingID, int PropertyID, string Address, string City, string State,
    string FilingType, DateTime DateFiled, string Status, string? ClaimantName,
    string? Notes, double? RiskScore);

public sealed record AiTriageRequestDto([property: Required, MinLength(10)] string RawText);

public sealed record AiExtractedClaimResponseDto(
    string? ClaimantName, string FilingType, double RiskScore, string LegalNotes, double ConfidenceScore);

public sealed record ChatbotResponseDto(
    string Message, bool RequiresConfirmation, AiExtractedClaimResponseDto? ExtractedClaim, FilingSummaryDto? SavedFiling);
