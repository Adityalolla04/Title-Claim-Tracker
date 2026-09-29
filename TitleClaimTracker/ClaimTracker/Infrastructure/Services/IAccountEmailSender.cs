namespace TitleClaimTracker.Infrastructure.Services;

public interface IAccountEmailSender
{
    bool IsConfigured { get; }
    Task SendPasswordResetAsync(string email, string? displayName, string resetUrl, CancellationToken cancellationToken);
}