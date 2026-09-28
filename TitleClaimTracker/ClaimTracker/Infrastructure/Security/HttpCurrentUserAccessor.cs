using System.Security.Claims;

namespace TitleClaimTracker.Infrastructure.Security;

public sealed class HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
    public bool IsAdministrator => IsAuthenticated && User!.IsInRole(AppRoles.Administrator);

    public string RequireUserId() => IsAuthenticated && !string.IsNullOrWhiteSpace(UserId)
        ? UserId!
        : throw new UnauthorizedAccessException("An authenticated account is required.");
}

public static class AppRoles
{
    public const string User = "User";
    public const string Administrator = "Admin";
}
