using System.ComponentModel.DataAnnotations;

namespace TitleClaimTracker.Core.DTOs;

public sealed record RegisterRequest(
    [property: Required, EmailAddress, MaxLength(256)] string Email,
    [property: Required, MinLength(12), MaxLength(128)] string Password,
    [property: MaxLength(200)] string? DisplayName);

public sealed record LoginRequest(
    [property: Required, EmailAddress, MaxLength(256)] string Email,
    [property: Required] string Password,
    bool RememberMe = false);

public sealed record ForgotPasswordRequest(
    [property: Required, EmailAddress, MaxLength(256)] string Email);

public sealed record ResetPasswordRequest(
    [property: Required, EmailAddress, MaxLength(256)] string Email,
    [property: Required, MaxLength(2048)] string Token,
    [property: Required, MinLength(12), MaxLength(128)] string NewPassword);

public sealed record CurrentAccountDto(string Id, string Email, string? DisplayName, IReadOnlyList<string> Roles);
