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
using TitleClaimTracker.ML.Services;

namespace TitleClaimTracker.Tests.UnitTests;

public sealed class Phase2WorkflowTests
{
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

    private static ChatbotTriageService CreateTriageService(TitleClaimDbContext context) => new(
        context,
        Mock.Of<ITitleClaimMlEngine>(),
        Mock.Of<IFilingService>(),
        new TestCurrentUserAccessor("admin-1", true),
        Mock.Of<IClaimExtractionService>(),
        new ConfigurationBuilder().AddInMemoryCollection().Build());

    private sealed class TestCurrentUserAccessor(string userId, bool isAdministrator) : ICurrentUserAccessor
    {
        public string? UserId => userId;
        public bool IsAuthenticated => true;
        public bool IsAdministrator => isAdministrator;
        public string RequireUserId() => userId;
    }
}
