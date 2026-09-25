using Microsoft.AspNetCore.Identity;
using TitleClaimTracker.Domain.Identity;

namespace TitleClaimTracker.Infrastructure.Security;

public static class IdentityBootstrapper
{
    public static async Task SeedConfiguredAdminAsync(IServiceProvider services, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Identity:BootstrapAdmin:Enabled"))
        {
            return;
        }

        var email = configuration["Identity:BootstrapAdmin:Email"];
        var password = configuration["Identity:BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Identity:BootstrapAdmin:Email and Identity:BootstrapAdmin:Password are required when bootstrap is enabled.");
        }

        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await EnsureRoleAsync(roleManager, AppRoles.User, cancellationToken);
        await EnsureRoleAsync(roleManager, AppRoles.Administrator, cancellationToken);

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedUtc = DateTime.UtcNow,
                IsActive = true
            };
            var createResult = await userManager.CreateAsync(user, password);
            EnsureSucceeded(createResult, "bootstrap administrator");
        }

        if (!await userManager.IsInRoleAsync(user, AppRoles.Administrator))
        {
            var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Administrator);
            EnsureSucceeded(roleResult, "administrator role assignment");
        }

        logger.LogInformation("Identity bootstrap verified the configured administrator account {Email}.", email);
    }

    private static async Task EnsureRoleAsync(RoleManager<IdentityRole> roleManager, string role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await roleManager.RoleExistsAsync(role))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new IdentityRole(role));
        if (!result.Succeeded && !await roleManager.RoleExistsAsync(role))
        {
            EnsureSucceeded(result, $"{role} role creation");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Identity {operation} failed: {errors}");
    }
}
