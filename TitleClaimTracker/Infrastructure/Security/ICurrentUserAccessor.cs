namespace TitleClaimTracker.Infrastructure.Security;

public interface ICurrentUserAccessor
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsAdministrator { get; }
    string RequireUserId();
}