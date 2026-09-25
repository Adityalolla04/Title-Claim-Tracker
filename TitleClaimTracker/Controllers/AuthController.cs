using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Identity;
using TitleClaimTracker.Infrastructure.Security;

namespace TitleClaimTracker.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<IdentityRole> roleManager,
    IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous, IgnoreAntiforgeryToken, HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        antiforgery.GetAndStoreTokens(HttpContext);
        return NoContent();
    }

    [AllowAnonymous, HttpPost("register")]
    public async Task<ActionResult<CurrentAccountDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (!await EnsureRoleAsync(AppRoles.User))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "The account role could not be provisioned." });
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName?.Trim(),
            CreatedUtc = DateTime.UtcNow,
            IsActive = true
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code.StartsWith("Duplicate", StringComparison.OrdinalIgnoreCase)))
            {
                return Conflict(new { error = "An account with that email already exists." });
            }

            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        }

        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.User);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "The account role could not be assigned." });
        }

        cancellationToken.ThrowIfCancellationRequested();
        await signInManager.SignInAsync(user, isPersistent: false);
        return Ok(await ToAccountAsync(user));
    }

    [AllowAnonymous, HttpPost("login")]
    public async Task<ActionResult<CurrentAccountDto>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        return Ok(await ToAccountAsync(user));
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [Authorize, HttpGet("me")]
    public async Task<ActionResult<CurrentAccountDto>> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            await signInManager.SignOutAsync();
            return Unauthorized();
        }

        return Ok(await ToAccountAsync(user));
    }

    private async Task<bool> EnsureRoleAsync(string role)
    {
        if (await roleManager.RoleExistsAsync(role))
        {
            return true;
        }

        var result = await roleManager.CreateAsync(new IdentityRole(role));
        return result.Succeeded || await roleManager.RoleExistsAsync(role);
    }

    private async Task<CurrentAccountDto> ToAccountAsync(ApplicationUser user) =>
        new(user.Id, user.Email ?? user.UserName ?? string.Empty, user.DisplayName, (await userManager.GetRolesAsync(user)).ToArray());
}
