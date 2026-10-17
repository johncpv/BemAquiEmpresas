using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BemAqui.BuildingBlocks.Email;

/// <summary>Seção <c>Email</c>. SMTP da plataforma; o SES entra pela interface SMTP dele. SMTP por tenant: CAD-06.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string FromAddress { get; set; } = "nao-responda@bemaquiapp.com.br";

    public string FromName { get; set; } = "BemAqui Empresas";

    public string SmtpHost { get; set; } = "localhost";

    public int SmtpPort { get; set; } = 587;

    public string? SmtpUsername { get; set; }

    /// <summary>Senha do SMTP (secret; nunca logada).</summary>
    public string? SmtpPassword { get; set; }

    /// <summary>None, StartTls ou SslOnConnect.</summary>
    public string Security { get; set; } = "StartTls";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed record OutgoingEmail(string To, RenderedEmail Content, string? LogoContentId = null, byte[]? Logo = null, string? LogoContentType = null);

public interface IEmailTransport
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

/// <summary>Envio por SMTP (MailKit). O logo vai inline (cid) a partir do armazenamento interno; nenhuma URL é buscada.</summary>
public sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var settings = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Content.Subject;

        var body = new BodyBuilder { HtmlBody = email.Content.Html, TextBody = email.Content.Text };
        if (email.Logo is { Length: > 0 } && email.LogoContentId is not null)
        {
            var logo = body.LinkedResources.Add("logo", email.Logo, ContentType.Parse(email.LogoContentType ?? "image/png"));
            logo.ContentId = email.LogoContentId;
        }

        message.Body = body.ToMessageBody();

        using var client = new SmtpClient { Timeout = (int)settings.Timeout.TotalMilliseconds };
        var security = Enum.TryParse<SecureSocketOptions>(settings.Security, ignoreCase: true, out var parsed) ? parsed : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.SmtpUsername))
        {
            await client.AuthenticateAsync(settings.SmtpUsername, settings.SmtpPassword ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
