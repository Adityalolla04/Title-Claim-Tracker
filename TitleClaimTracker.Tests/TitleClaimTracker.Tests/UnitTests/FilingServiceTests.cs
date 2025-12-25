using System;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Data;
using TitleClaimTracker.Services;
using TitleClaimTracker.Models;
using System.Collections.Generic;

namespace TitleClaimTracker.Tests.UnitTests
{
    public class FilingServiceTests
    {
        private ApplicationDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new ApplicationDbContext(options);
            context.Database.EnsureCreated();

            // Seed required FK data for all tests
            context.FilingTypes.Add(new FilingType { FilingTypeID = 1, TypeName = "Test Type" });
            context.Properties.Add(new Property { PropertyID = 1, Address = "Dummy", City = "Dummy", State = "TX", LegalDescription = "Dummy" });
            context.SaveChanges();

            return context;
        }

        [Fact]
        public async Task CreateFiling_ShouldSaveRecord_WhenAllRequiredFieldsArePresent()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new FilingService(context);

            var newFiling = new LegalFiling
            {
                ClaimantName = "John Tester",
                DateFiled = DateTime.Now,
                Status = "New",
                FilingTypeID = 1,
                // Tested with required property fields
                Property = new Property
                {
                    Address = "123 Test St",
                    City = "Testville",
                    State = "TX",
                    LegalDescription = "Lot 42, Block B, Test Subdivision"
                },
                // Tested with required Notes field for LegalFiling
                Notes = "Initial test case."
            };

            // Act
            int createdId = await service.CreateFilingAndPropertyAsync(newFiling);

            // Assert
            Assert.True(createdId > 0);
            var savedFiling = await context.LegalFilings.FirstOrDefaultAsync(f => f.FilingID == createdId);
            Assert.NotNull(savedFiling);
        }

        [Fact]
        public async Task GetFilingById_ShouldThrowException_WhenIdDoesNotExist()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new FilingService(context);
            int invalidId = 99999;

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            {
                await service.GetFilingByIdAsync(invalidId);
            });
        }

        [Fact]
        public async Task UpdateStatus_ShouldThrowError_IfStatusIsInvalid()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new FilingService(context);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await service.UpdateFilingStatusAsync(1, "Archived");
            });
        }
        [Fact]
        public async Task DeleteFiling_ShouldRemoveRecord_WhenIdExists()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new FilingService(context);
            var filingToDelete = new LegalFiling
            {
                ClaimantName = "Delete Me",
                DateFiled = DateTime.Now,
                Status = "New",
                FilingTypeID = 1,
                PropertyID = 1,
                Notes = "To be deleted."
            };
            context.LegalFilings.Add(filingToDelete);
            await context.SaveChangesAsync();
            // Act
            bool deleteResult = await service.DeleteFilingAsync(filingToDelete.FilingID);
            // Assert
            Assert.True(deleteResult);
            var deletedFiling = await context.LegalFilings.FindAsync(filingToDelete.FilingID);
            Assert.Null(deletedFiling);
        }
    }
}