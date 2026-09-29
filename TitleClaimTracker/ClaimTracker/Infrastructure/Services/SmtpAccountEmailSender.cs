using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.WebUtilities;
using MimeKit;

namespace TitleClaimTracker.Infrastructure.Services;

public sealed class SmtpAccountEmailSender(IConfiguration configuration) : IAccountEmailSender
{
    private string? Host => configuration["Email:Smtp:Host"];
    private string? Username => configuration["Email:Smtp:Username"];
    private string? Password => configuration["Email:Smtp:Password"];
    private string? FromAddress => configuration["Email:Smtp:FromAddress"];
    private string FromName => configuration["Email:Smtp:FromName"] ?? "Title Claim Tracker";
    private int Port => configuration.GetValue("Email:Smtp:Port", 587);
    private bool UseSslOnConnect => configuration.GetValue("Email:Smtp:UseSslOnConnect", false);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(FromAddress);

    public async Task SendPasswordResetAsync(string email, string? displayName, string resetUrl, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("SMTP password-reset email is not configured.");
        }

        var host = Host!;
        var username = Username!;
        var password = Password!;
        var fromAddress = FromAddress!;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(FromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = "Reset your Title Claim Tracker password";
        message.Body = new TextPart("plain")
        {
            Text = $"Hello {displayName ?? "there"},\n\n" +
                "We received a request to reset your Title Claim Tracker password. " +
                "Use the link below within one hour. If you did not request this, you can ignore this email.\n\n" +
                $"{resetUrl}\n"
        };

        using var client = new SmtpClient();
        var socketOptions = UseSslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(host, Port, socketOptions, cancellationToken);
        try
        {
            await client.AuthenticateAsync(username, password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }
}