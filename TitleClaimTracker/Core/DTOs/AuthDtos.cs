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

public sealed record CurrentAccountDto(string Id, string Email, string? DisplayName, IReadOnlyList<string> Roles);
