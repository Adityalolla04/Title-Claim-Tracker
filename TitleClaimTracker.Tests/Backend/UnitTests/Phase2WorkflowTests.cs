using System.Text.Json;
using System.Globalization;
using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Core.Validation;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Security;
using TitleClaimTracker.Infrastructure.Services;
using TitleClaimTracker.ML.Models;
using TitleClaimTracker.ML.Services;

namespace TitleClaimTracker.Tests.UnitTests;

public sealed class Phase2WorkflowTests
{
    [Fact]
    public void ClaimExtraction_SerializesClassifierScoreWithoutCallingItConfidence()
    {
        var extraction = new ClaimExtractionDto("A. Owner", "Lien", .2, "Issue details", .74);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(extraction, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(.74, document.RootElement.GetProperty("classificationScore").GetDouble());
        Assert.False(document.RootElement.TryGetProperty("confidenceScore", out _));
    }

    [Fact]
    public void UpdateClaimValidator_RejectsInvalidRowVersion()
    {
        var validator = new UpdateClaimValidator();
        var request = new UpdateClaimDto("1 Main Street", "Austin", "TX", null, "Title Claim", DateTime.UtcNow, null, null, "not-base64");

        var result = validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.RowVersion);
    }

    [Fact]
    public void SubmitTriageDraftValidator_RequiresAttemptAndAddressFields()
    {
        var validator = new SubmitTriageDraftValidator();
        var request = new SubmitTriageDraftDto(0, string.Empty, string.Empty, "T", null, string.Empty, null, null);

        var result = validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.TriageAttemptID);
        result.ShouldHaveValidationErrorFor(x => x.Address);
        result.ShouldHaveValidationErrorFor(x => x.City);
        result.ShouldHaveValidationErrorFor(x => x.State);
        result.ShouldHaveValidationErrorFor(x => x.FilingType);
    }

    [Fact]
    public async Task TriageAsync_HighScoreAndCompleteLocation_PreparesDraftWithoutCreatingClaim()
    {
        await using var context = CreateContext();
        context.FilingTypes.Add(new FilingType { FilingTypeID = 1, TypeName = "Lien" });
        var filingService = new Mock<IFilingService>();
        var service = CreateTriageService(context, .92f, CreateExtraction(), filingService: filingService.Object);

        var response = await service.TriageAsync(new ChatTriageRequestDto("My property has a lien dispute"), CancellationToken.None);

        Assert.Equal("needsReview", response.Outcome);
        Assert.True(response.RequiresConfirmation);
        Assert.Null(response.SavedFiling);
        Assert.Equal((double).92f, response.ExtractedClaim?.ClassificationScore);
        Assert.Equal("A. Owner", response.ExtractedClaim?.ClaimantName);
        Assert.Equal("1 Main Street", response.ExtractedClaim?.Address);
        Assert.Equal("Austin", response.ExtractedClaim?.City);
        Assert.Equal("TX", response.ExtractedClaim?.State);
        Assert.Equal("AwaitingConfirmation", Assert.Single(context.TriageAttempts).Outcome);
        filingService.Verify(x => x.CreateAsync(It.IsAny<CreateClaimDto>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<byte?>(), It.IsAny<string?>(), It.IsAny<long?>()), Times.Never);
    }

    [Fact]
    public async Task TriageAsync_ClaimantReceivesNoModelOrRiskScores()
    {
        await using var context = CreateContext();
        context.FilingTypes.Add(new FilingType { FilingTypeID = 1, TypeName = "Lien" });
        var service = CreateTriageService(context, isAdministrator: false);

        var response = await service.TriageAsync(new ChatTriageRequestDto("My property has a lien dispute"), CancellationToken.None);

        Assert.NotNull(response.ExtractedClaim);
        Assert.Null(response.ExtractedClaim.ClassificationScore);
        Assert.Null(response.ExtractedClaim.RiskScore);
    }

    [Fact]
    public async Task TriageAsync_LowScoreWithCompleteLocation_RoutesToReview()
    {
        await using var context = CreateContext();
        context.FilingTypes.Add(new FilingType { FilingTypeID = 1, TypeName = "Lien" });
        var service = CreateTriageService(context, .40f, CreateExtraction());

        var response = await service.TriageAsync(new ChatTriageRequestDto("My property has a lien dispute"), CancellationToken.None);

        Assert.Equal("needsReview", response.Outcome);
        Assert.Contains("not a legal correctness probability", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(.40m, Assert.Single(context.TriageAttempts).ClassificationScore);
    }

    [Fact]
    public async Task TriageAsync_MissingLocation_ReturnsNeedsInformationEvenWithHighScore()
    {
        await using var context = CreateContext();
        context.FilingTypes.Add(new FilingType { FilingTypeID = 1, TypeName = "Lien" });
        var service = CreateTriageService(context, .99f, CreateExtraction(address: null, city: null, state: null));

        var response = await service.TriageAsync(new ChatTriageRequestDto("My property has a lien dispute"), CancellationToken.None);

        Assert.Equal("needsInformation", response.Outcome);
        Assert.Contains("property address, city, two-letter state", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.SavedFiling);
    }

    [Fact]
    public async Task TriageAsync_UnsupportedCategory_RejectsAndPersistsAttempt()
    {
        await using var context = CreateContext();
        var service = CreateTriageService(context, .99f, CreateExtraction(), "Other");

        var response = await service.TriageAsync(new ChatTriageRequestDto("My property has a lien dispute"), CancellationToken.None);

        Assert.Equal("outOfScope", response.Outcome);
        Assert.False(response.RequiresConfirmation);
        Assert.Equal("Rejected", Assert.Single(context.TriageAttempts).Outcome);
    }

    [Fact]
    public async Task SubmitDraftAsync_MatchingHighScore_RecordsAssistedCreation()
    {
        await using var context = CreateContext();
        context.FilingTypes.Add(new FilingType { FilingTypeID = 1, TypeName = "Lien" });
        context.TriageAttempts.Add(CreateAttempt(.92m));
        await context.SaveChangesAsync();
        var expected = new ClaimSummaryDto(5, 8, "1 Main Street", "Austin", "TX", "Lien", DateTime.UtcNow, "New", null, null, null);
        var filingService = new Mock<IFilingService>();
        filingService.Setup(x => x.CreateAsync(It.IsAny<CreateClaimDto>(), It.IsAny<CancellationToken>(), "Assisted", .92m, 3, "triage-v1", null)).ReturnsAsync(expected);
        var service = CreateTriageService(context, filingService: filingService.Object);

        var result = await service.SubmitDraftAsync(CreateSubmitRequest("Lien"), CancellationToken.None);

        Assert.Equal(expected.FilingID, result.FilingID);
        Assert.Equal("Created", (await context.TriageAttempts.SingleAsync()).Outcome);
        filingService.VerifyAll();
    }

    [Fact]
    public async Task SubmitDraftAsync_ChangedCategory_RecordsManualCreationWithoutClassifierScore()
    {
        await using var context = CreateContext();
        context.FilingTypes.Add(new FilingType { FilingTypeID = 1, TypeName = "Lien" });
        context.TriageAttempts.Add(CreateAttempt(.92m));
        await context.SaveChangesAsync();
        var expected = new ClaimSummaryDto(6, 9, "1 Main Street", "Austin", "TX", "Title Claim", DateTime.UtcNow, "New", null, null, null);
        var filingService = new Mock<IFilingService>();
        filingService.Setup(x => x.CreateAsync(It.IsAny<CreateClaimDto>(), It.IsAny<CancellationToken>(), "Manual", null, 3, "triage-v1", null)).ReturnsAsync(expected);
        var service = CreateTriageService(context, filingService: filingService.Object);

        var result = await service.SubmitDraftAsync(CreateSubmitRequest("Title Claim"), CancellationToken.None);

        Assert.Equal(expected.FilingID, result.FilingID);
        filingService.VerifyAll();
    }

    [Fact]
    public async Task IncorrectFeedback_RequiresExistingCorrectedFilingType()
    {
        await using var context = CreateContext();
        context.TriageAttempts.Add(new TriageAttempt
        {
            TriageAttemptID = 1,
            SubmittedByUserId = "user-1",
            InputText = "address: 1 Main Street, city: Austin, state: TX",
            TriageRuleVersion = "triage-v1"
        });
        await context.SaveChangesAsync();
        var service = CreateTriageService(context);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.RecordFeedbackAsync(1, new TriageFeedbackDto(1, false, null, null, null), CancellationToken.None));

        Assert.Contains("corrected filing type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Feedback_CannotBeRecordedTwice()
    {
        await using var context = CreateContext();
        context.TriageAttempts.Add(new TriageAttempt
        {
            TriageAttemptID = 1,
            SubmittedByUserId = "user-1",
            InputText = "address: 1 Main Street, city: Austin, state: TX",
            TriageRuleVersion = "triage-v1"
        });
        context.TriageFeedback.Add(new TriageFeedback
        {
            TriageFeedbackID = 1,
            TriageAttemptID = 1,
            ReviewedByUserId = "admin-1",
            WasCorrect = true,
            ReviewedUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = CreateTriageService(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordFeedbackAsync(1, new TriageFeedbackDto(1, true, null, null, null), CancellationToken.None));

        Assert.Contains("already been reviewed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TitleClaimDbContext CreateContext() => new(new DbContextOptionsBuilder<TitleClaimDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static ChatbotTriageService CreateTriageService(
        TitleClaimDbContext context,
        float score = .92f,
        StructuredClaimExtraction? extraction = null,
        string predictedType = "Lien",
        IFilingService? filingService = null,
        bool isAdministrator = true)
    {
        var mlEngine = new Mock<ITitleClaimMlEngine>();
        mlEngine.Setup(x => x.Predict(It.IsAny<string>())).Returns(new ClaimPrediction { PredictedFilingType = predictedType, Probability = score, CalculatedRisk = .55f });
        var extractionService = new Mock<IClaimExtractionService>();
        extractionService.Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(extraction ?? CreateExtraction());
        var settings = new Dictionary<string, string?> { ["AiService:MinimumConfidence"] = .85m.ToString(CultureInfo.InvariantCulture) };

        return new ChatbotTriageService(
            context,
            mlEngine.Object,
            filingService ?? Mock.Of<IFilingService>(),
            new TestCurrentUserAccessor(isAdministrator ? "admin-1" : "user-1", isAdministrator),
            extractionService.Object,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static StructuredClaimExtraction CreateExtraction(string? address = "1 Main Street", string? city = "Austin", string? state = "TX") => new(
        "deterministic", "A. Owner", address, city, state, "Lien dispute", [], [], [], "Lien dispute intake", "A. Owner", address, city, state);

    private static TriageAttempt CreateAttempt(decimal score) => new()
    {
        TriageAttemptID = 1,
        SubmittedByUserId = "admin-1",
        InputText = "My property has a lien dispute",
        PredictedFilingTypeID = 1,
        ClassificationScore = score,
        HeuristicPriority = 3,
        TriageRuleVersion = "triage-v1",
        Outcome = "AwaitingConfirmation"
    };

    private static SubmitTriageDraftDto CreateSubmitRequest(string typeName) => new(
        1, "1 Main Street", "Austin", "TX", null, typeName, "A. Owner", "Lien dispute");

    private sealed class TestCurrentUserAccessor(string userId, bool isAdministrator) : ICurrentUserAccessor
    {
        public string? UserId => userId;
        public bool IsAuthenticated => true;
        public bool IsAdministrator => isAdministrator;
        public string RequireUserId() => userId;
    }
}
