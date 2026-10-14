using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace KuTax.Infrastructure.Email;

public sealed record EmailMessage(string To, string Subject, string Text, string Html);

public interface IEmailSender
{
    /// <summary>False when email isn't set up here; callers then show the link to share some other way.</summary>
    bool Enabled { get; }
    Task<bool> SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// Sends through any SMTP provider (Email:Host, Port, Username, Password, From, FromName; Security is StartTls,
/// SslOnConnect or None for a local test server). Failures are logged without the address and reported as not sent.
/// </summary>
public sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public bool Enabled => !string.IsNullOrWhiteSpace(configuration["Email:Host"]) && !string.IsNullOrWhiteSpace(configuration["Email:From"]);

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (!Enabled) return false;
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(configuration["Email:FromName"] ?? "KuTax", configuration["Email:From"]!));
        mail.To.Add(MailboxAddress.Parse(message.To));
        mail.Subject = message.Subject;
        mail.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();
        var security = configuration["Email:Security"] switch
        {
            "None" => SecureSocketOptions.None,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls,
        };
        try
        {
            using var client = new SmtpClient { Timeout = 15000 };
            await client.ConnectAsync(configuration["Email:Host"]!, configuration.GetValue("Email:Port", 587), security, ct);
            if (configuration["Email:Username"] is { Length: > 0 } user) await client.AuthenticateAsync(user, configuration["Email:Password"] ?? "", ct);
            await client.SendAsync(mail, ct);
            await client.DisconnectAsync(true, ct);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning("Email could not be sent ({ExceptionType}).", error.GetType().Name);
            return false;
        }
    }
}
