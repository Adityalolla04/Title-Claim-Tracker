using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Identity;
using TitleClaimTracker.Infrastructure.Security;
using TitleClaimTracker.Infrastructure.Services;

namespace TitleClaimTracker.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<IdentityRole> roleManager,
    IAntiforgery antiforgery,
    IAccountEmailSender emailSender,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
{
    [AllowAnonymous, IgnoreAntiforgeryToken, HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });
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
        if (user is null || !user.IsActive || await userManager.IsInRoleAsync(user, AppRoles.Administrator))
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

    [AllowAnonymous, HttpPost("admin-login")]
    public async Task<ActionResult<CurrentAccountDto>> AdminLogin(LoginRequest request)
    {
        var email = request.Email.Trim();
        var allowedEmail = configuration["Identity:AdminAccess:Email"]?.Trim();
        if (string.IsNullOrWhiteSpace(allowedEmail) || !string.Equals(email, allowedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized();
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive || !await userManager.IsInRoleAsync(user, AppRoles.Administrator))
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

    [AllowAnonymous, HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Password reset email is not configured. Contact the administrator." });
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not null && user.IsActive)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var baseUrl = configuration["PasswordReset:ClientBaseUrl"] ?? "http://localhost:4200";
            var resetUrl = QueryHelpers.AddQueryString($"{baseUrl.TrimEnd('/')}/reset-password", new Dictionary<string, string?>
            {
                ["email"] = user.Email ?? user.UserName ?? request.Email.Trim(),
                ["token"] = token
            });

            try
            {
                await emailSender.SendPasswordResetAsync(user.Email ?? request.Email.Trim(), user.DisplayName, resetUrl, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Password reset email delivery failed for account {UserId}.", user.Id);
            }
        }

        return Ok(new { message = "If an active account matches that email, password reset instructions will be sent." });
    }

    [AllowAnonymous, HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return BadRequest(new { error = "The reset link is invalid or expired, or the password does not meet the requirements." });
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = "The reset link is invalid or expired, or the password does not meet the requirements." });
        }

        return Ok(new { message = "Password reset successfully. You can now sign in." });
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
