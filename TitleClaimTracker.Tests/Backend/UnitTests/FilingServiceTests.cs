using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Repositories;
using TitleClaimTracker.Infrastructure.Security;
using TitleClaimTracker.Infrastructure.Services;

namespace TitleClaimTracker.Tests.UnitTests;

public sealed class FilingRepositorySecurityTests
{
    [Fact]
    public async Task RegularUser_CanOnlyReadOwnActiveFilings()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var repository = new FilingRepository(context);

        var result = await repository.SearchAsync(null, null, null, null, 1, 25, "user-a", false, CancellationToken.None);

        var filing = Assert.Single(result.Items);
        Assert.Equal(1, filing.FilingID);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task RegularUser_CannotLookupAnotherUsersOrSoftDeletedFiling()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var repository = new FilingRepository(context);

        Assert.Null(await repository.GetByIdAsync(2, "user-a", false, CancellationToken.None));
        Assert.Null(await repository.GetByIdAsync(4, "user-a", false, CancellationToken.None));
    }

    [Fact]
    public async Task Administrator_CanReadActiveUnassignedAndCrossUserFilings()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var repository = new FilingRepository(context);

        var result = await repository.SearchAsync(null, null, null, null, 1, 25, "admin", true, CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(new[] { 3, 2, 1 }, result.Items.Select(item => item.FilingID).ToArray());
    }

    [Fact]
    public async Task Search_FindsClaimsByClaimantNameOrClaimNumber_WithinCurrentUsersScope()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var repository = new FilingRepository(context);

        var byName = await repository.SearchAsync(null, null, null, "avery", 1, 25, "user-a", false, CancellationToken.None);
        var byNumber = await repository.SearchAsync(null, null, null, "#1", 1, 25, "user-a", false, CancellationToken.None);

        Assert.Equal(new[] { 1 }, byName.Items.Select(item => item.FilingID));
        Assert.Equal(new[] { 1 }, byNumber.Items.Select(item => item.FilingID));
    }

    [Fact]
    public async Task Analytics_ContainsOnlyTheSignedInClaimantsRecords()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var service = new FilingService(context, new FilingRepository(context), new TestCurrentUserAccessor("user-a", false), NullLogger<FilingService>.Instance);

        var analytics = await service.GetAnalyticsAsync(CancellationToken.None);

        Assert.Equal(1, analytics.TotalClaims);
        Assert.Equal(1, analytics.OpenClaims);
        Assert.Equal(0, analytics.ResolvedClaims);
        Assert.Equal(1, Assert.Single(analytics.StatusBreakdown).Count);
        Assert.Equal(1, Assert.Single(analytics.TypeBreakdown).Count);
    }

    [Fact]
    public async Task Analytics_ForAdministratorIncludesAllActiveClaimants()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var service = new FilingService(context, new FilingRepository(context), new TestCurrentUserAccessor("admin", true), NullLogger<FilingService>.Instance);

        var analytics = await service.GetAnalyticsAsync(CancellationToken.None);

        Assert.Equal(3, analytics.TotalClaims);
        Assert.Equal(3, analytics.OpenClaims);
    }

    [Fact]
    public async Task Analytics_MonthlyFiledAndSolvedCountsRespectOwnershipAndCountEachClaimOnce()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var today = DateTime.UtcNow.Date;
        context.LegalFilings.Single(item => item.FilingID == 1).DateFiled = today;
        context.LegalFilings.Single(item => item.FilingID == 2).DateFiled = today;
        context.ClaimStatusHistory.AddRange(
            new ClaimStatusHistory { FilingID = 1, FromStatus = "New", ToStatus = "Resolved", TransitionedUtc = DateTime.UtcNow.AddDays(-2) },
            new ClaimStatusHistory { FilingID = 1, FromStatus = "Resolved", ToStatus = "Closed", TransitionedUtc = DateTime.UtcNow.AddDays(-1) },
            new ClaimStatusHistory { FilingID = 2, FromStatus = "New", ToStatus = "Closed", TransitionedUtc = DateTime.UtcNow.AddDays(-1) },
            new ClaimStatusHistory { FilingID = 4, FromStatus = "New", ToStatus = "Resolved", TransitionedUtc = DateTime.UtcNow.AddDays(-1) });
        await context.SaveChangesAsync();

        var claimant = await new FilingService(context, new FilingRepository(context), new TestCurrentUserAccessor("user-a", false), NullLogger<FilingService>.Instance)
            .GetAnalyticsAsync(CancellationToken.None);
        var administrator = await new FilingService(context, new FilingRepository(context), new TestCurrentUserAccessor("admin", true), NullLogger<FilingService>.Instance)
            .GetAnalyticsAsync(CancellationToken.None);
        var currentMonth = DateTime.UtcNow.Month;
        var claimantMonth = Assert.Single(claimant.MonthlySubmissions.Where(item => item.Year == DateTime.UtcNow.Year && item.Month == currentMonth));
        var administratorMonth = Assert.Single(administrator.MonthlySubmissions.Where(item => item.Year == DateTime.UtcNow.Year && item.Month == currentMonth));

        Assert.Equal(1, claimantMonth.Count);
        Assert.Equal(1, claimantMonth.SolvedCount);
        Assert.Equal(2, administratorMonth.Count);
        Assert.Equal(2, administratorMonth.SolvedCount);
    }

    [Fact]
    public async Task Claimant_CanEditOwnNewClaim()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var service = new FilingService(context, new FilingRepository(context), new TestCurrentUserAccessor("user-a", false), NullLogger<FilingService>.Instance);
        var request = new UpdateClaimDto("1 Main Street", "Austin", "TX", null, "Title Claim", new DateTime(2025, 1, 1), "Avery Morgan", "Updated description", Convert.ToBase64String([1]));

        var updated = await service.UpdateAsync(1, request, CancellationToken.None);

        Assert.Equal("Updated description", updated.Notes);
    }

    [Fact]
    public async Task Claimant_CannotEditOrDeleteClaimAfterReviewBegins()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        context.LegalFilings.Single(item => item.FilingID == 1).Status = "Under Review";
        var service = new FilingService(context, new FilingRepository(context), new TestCurrentUserAccessor("user-a", false), NullLogger<FilingService>.Instance);
        var request = new UpdateClaimDto("1 Main Street", "Austin", "TX", null, "Title Claim", new DateTime(2025, 1, 1), "Avery Morgan", "Updated description", Convert.ToBase64String([1]));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(1, request, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteAsync(1, Convert.ToBase64String([1]), CancellationToken.None));
    }

    [Fact]
    public async Task Claimant_CanSoftDeleteOwnNewClaim()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var service = new FilingService(context, new FilingRepository(context), new TestCurrentUserAccessor("user-a", false), NullLogger<FilingService>.Instance);

        await service.DeleteAsync(1, Convert.ToBase64String([1]), CancellationToken.None);

        Assert.Null(await new FilingRepository(context).GetByIdAsync(1, "user-a", false, CancellationToken.None));
    }

    private static TitleClaimDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TitleClaimDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TitleClaimDbContext(options);
    }

    private static void SeedFilings(TitleClaimDbContext context)
    {
        var property = new Property { PropertyID = 1, Address = "1 Main Street", City = "Austin", State = "TX" };
        var filingType = new FilingType { FilingTypeID = 1, TypeName = "Title Claim" };
        context.Properties.Add(property);
        context.FilingTypes.Add(filingType);
        context.LegalFilings.AddRange(
            CreateFiling(1, "user-a", property, filingType, false),
            CreateFiling(2, "user-b", property, filingType, false),
            CreateFiling(3, null, property, filingType, false),
            CreateFiling(4, "user-a", property, filingType, true));
        context.SaveChanges();
    }

    private static LegalFiling CreateFiling(int id, string? owner, Property property, FilingType filingType, bool isDeleted) => new()
    {
        FilingID = id,
        PropertyID = property.PropertyID,
        FilingTypeID = filingType.FilingTypeID,
        Property = property,
        FilingType = filingType,
        DateFiled = new DateTime(2025, 1, id),
        Status = "New",
        RowVersion = [1],
        SubmittedByUserId = owner,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
        IsDeleted = isDeleted,
        ClaimantName = id is 1 or 2 ? "Avery Morgan" : null,
        DeletedUtc = isDeleted ? DateTime.UtcNow : null,
        DeletedByUserId = isDeleted ? "admin" : null,
    };

    private sealed class TestCurrentUserAccessor(string userId, bool isAdministrator) : ICurrentUserAccessor
    {
        public string? UserId => userId;
        public bool IsAuthenticated => true;
        public bool IsAdministrator => isAdministrator;
        public string RequireUserId() => userId;
    }
}
