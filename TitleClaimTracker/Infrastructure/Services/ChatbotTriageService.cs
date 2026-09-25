using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Security;
using TitleClaimTracker.ML.Services;

namespace TitleClaimTracker.Infrastructure.Services;

public sealed partial class ChatbotTriageService(
    TitleClaimDbContext db,
    ITitleClaimMlEngine mlEngine,
    IFilingService filingService,
    ICurrentUserAccessor currentUser,
    IClaimExtractionService extractionService,
    IConfiguration configuration) : IChatbotTriageService
{
    public async Task<ChatbotResponseDto> TriageAsync(ChatTriageRequestDto request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var prediction = mlEngine.Predict(request.Message);
        var extracted = await extractionService.ExtractAsync(request.Message, cancellationToken);
        var claimant = extracted.ClaimantName;
        var address = extracted.Address;
        var city = extracted.City;
        var state = extracted.State;
        var predictedType = await db.FilingTypes.SingleOrDefaultAsync(x => x.TypeName == prediction.PredictedFilingType, cancellationToken);
        var minimumConfidence = configuration.GetValue<decimal?>("AiService:MinimumConfidence") ?? .85m;
        var isInScope = LegalFilingCategories.Contains(prediction.PredictedFilingType, StringComparer.OrdinalIgnoreCase);
        var hasRequiredLocation = address is not null && city is not null && state is not null;
        var outcome = !isInScope ? "Rejected" : "AwaitingConfirmation";
        var attempt = new TriageAttempt
        {
            SubmittedByUserId = userId,
            InputText = request.Message.Trim(),
            PredictedFilingTypeID = predictedType?.FilingTypeID,
            ClassificationScore = Math.Clamp((decimal)prediction.Probability, 0m, 1m),
            HeuristicPriority = CalculatePriority(prediction.CalculatedRisk),
            TriageRuleVersion = "triage-v1",
            Outcome = outcome,
            ExtractedClaimantName = claimant,
            ExtractedAddress = address,
            ExtractedCity = city,
            ExtractedState = state,
            ExtractedNotes = extracted.IssueDescription ?? request.Message.Trim(),
            CompletedUtc = outcome == "Rejected" ? DateTime.UtcNow : null
        };
        db.TriageAttempts.Add(attempt);

        var extraction = new ClaimExtractionDto(claimant, prediction.PredictedFilingType, prediction.CalculatedRisk, extracted.IssueDescription ?? request.Message.Trim(), prediction.Probability);
        await db.SaveChangesAsync(cancellationToken);
        if (!isInScope)
        {
            return new ChatbotResponseDto("This description is outside the supported Title Claim, Lien, Easement, and Deed Dispute intake categories. No claim was created.", false, extraction, null, attempt.TriageAttemptID, "outOfScope", extracted.Mode);
        }

        if ((decimal)prediction.Probability < minimumConfidence || !hasRequiredLocation)
        {
            var missing = string.Join(", ", new[] { address is null ? "property address" : null, city is null ? "city" : null, state is null ? "two-letter state" : null }.Where(x => x is not null));
            var detail = missing.Length == 0 ? "Review and correct the intake draft before explicitly submitting it." : $"Provide the {missing} and then explicitly submit the draft.";
            var uiOutcome = missing.Length == 0 ? "needsReview" : "needsInformation";
            return new ChatbotResponseDto($"The classifier selected {prediction.PredictedFilingType}; its score is not a legal correctness probability. {detail}", true, extraction, null, attempt.TriageAttemptID, uiOutcome, extracted.Mode);
        }

        return new ChatbotResponseDto("Your assisted intake draft is ready. Review the extracted details and explicitly submit it to create a claim.", true, extraction, null, attempt.TriageAttemptID, "needsReview", extracted.Mode);
    }

    public async Task<ClaimSummaryDto> SubmitDraftAsync(SubmitTriageDraftDto request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var attempt = await db.TriageAttempts.Include(x => x.PredictedFilingType).Include(x => x.Feedback).SingleOrDefaultAsync(x => x.TriageAttemptID == request.TriageAttemptID && x.SubmittedByUserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("The intake draft was not found.");
        if (attempt.CreatedFilingID.HasValue)
        {
            var existing = await filingService.GetAsync(attempt.CreatedFilingID.Value, cancellationToken);
            return new ClaimSummaryDto(existing.FilingID, existing.PropertyID, existing.Address, existing.City, existing.State, existing.FilingType, existing.DateFiled, existing.Status, existing.ClaimantName, existing.Notes, existing.RiskScore);
        }

        var score = attempt.ClassificationScore;
        var acceptedAssisted = string.Equals(attempt.PredictedFilingType?.TypeName, request.FilingType, StringComparison.OrdinalIgnoreCase)
            && score.HasValue
            && score.Value >= (configuration.GetValue<decimal?>("AiService:MinimumConfidence") ?? .85m);
        var create = new CreateClaimDto(request.Address, request.City, request.State, request.LegalDescription, request.FilingType, DateTime.UtcNow, request.ClaimantName, request.Notes, null);
        try
        {
            var saved = await filingService.CreateAsync(create, cancellationToken, acceptedAssisted ? "Assisted" : "Manual", acceptedAssisted ? score : null, attempt.HeuristicPriority, attempt.TriageRuleVersion, attempt.ModelVersionID);
            attempt.CreatedFilingID = saved.FilingID;
            attempt.Outcome = "Created";
            attempt.CompletedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return saved;
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            attempt.Outcome = "Failed";
            attempt.FailureCode = exception.GetType().Name;
            attempt.FailureMessage = "The submitted draft did not pass server validation.";
            attempt.CompletedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<TriageAttemptDto>> GetReviewQueueAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdministrator) throw new UnauthorizedAccessException("Only administrators can review intake attempts.");
        return await db.TriageAttempts.AsNoTracking().Include(x => x.PredictedFilingType)
            .Where(x => x.Outcome == "AwaitingConfirmation" && x.Feedback == null)
            .OrderByDescending(x => x.CreatedUtc).Take(200)
            .Select(x => new TriageAttemptDto(x.TriageAttemptID, x.Outcome, x.Outcome == "Rejected" ? "outOfScope" : x.Outcome == "Failed" ? "failed" : "needsReview", x.PredictedFilingType == null ? null : x.PredictedFilingType.TypeName, x.ClassificationScore, x.HeuristicPriority, x.ExtractedAddress, x.ExtractedCity, x.ExtractedState, x.ExtractedClaimantName, x.ExtractedNotes, x.CreatedFilingID, x.CreatedUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task RecordFeedbackAsync(long attemptId, TriageFeedbackDto request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdministrator) throw new UnauthorizedAccessException("Only administrators can correct intake attempts.");
        var attempt = await db.TriageAttempts.Include(x => x.Feedback).SingleOrDefaultAsync(x => x.TriageAttemptID == attemptId, cancellationToken) ?? throw new KeyNotFoundException("The intake attempt was not found.");
        if (attempt.Feedback is not null) throw new InvalidOperationException("This intake attempt has already been reviewed.");
        if (!request.WasCorrect)
        {
            if (!request.CorrectedFilingTypeID.HasValue)
            {
                throw new ArgumentException("A corrected filing type is required when the prediction was incorrect.", nameof(request));
            }

            var correctedTypeExists = await db.FilingTypes.AnyAsync(x => x.FilingTypeID == request.CorrectedFilingTypeID.Value, cancellationToken);
            if (!correctedTypeExists)
            {
                throw new ArgumentException("The corrected filing type does not exist.", nameof(request));
            }
        }

        db.TriageFeedback.Add(new TriageFeedback { TriageAttemptID = attempt.TriageAttemptID, ReviewedByUserId = currentUser.RequireUserId(), WasCorrect = request.WasCorrect, CorrectedFilingTypeID = request.CorrectedFilingTypeID, CorrectedIssueTags = request.CorrectedIssueTags, CorrectionNotes = request.CorrectionNotes, ReviewedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static readonly string[] LegalFilingCategories = ["Title Claim", "Lien", "Easement", "Deed Dispute"];

    private static byte CalculatePriority(float riskScore) => riskScore switch
    {
        >= .8f => 1,
        >= .6f => 2,
        >= .4f => 3,
        >= .2f => 4,
        _ => 5
    };

}