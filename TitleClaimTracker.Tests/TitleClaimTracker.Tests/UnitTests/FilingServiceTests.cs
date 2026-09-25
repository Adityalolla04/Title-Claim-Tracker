using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Repositories;

namespace TitleClaimTracker.Tests.UnitTests;

public sealed class FilingRepositorySecurityTests
{
    [Fact]
    public async Task RegularUser_CanOnlyReadOwnActiveFilings()
    {
        await using var context = CreateContext();
        SeedFilings(context);
        var repository = new FilingRepository(context);

        var result = await repository.SearchAsync(null, null, null, 1, 25, "user-a", false, CancellationToken.None);

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

        var result = await repository.SearchAsync(null, null, null, 1, 25, "admin", true, CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(new[] { 3, 2, 1 }, result.Items.Select(item => item.FilingID).ToArray());
    }

    private static TitleClaimDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TitleClaimDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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
        SubmittedByUserId = owner,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
        IsDeleted = isDeleted,
        DeletedUtc = isDeleted ? DateTime.UtcNow : null,
        DeletedByUserId = isDeleted ? "admin" : null,
    };
}
