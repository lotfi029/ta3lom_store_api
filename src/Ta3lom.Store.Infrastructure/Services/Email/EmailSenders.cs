using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Email;

/// `Store:Email`. `Mode = sink` writes `.eml` files (Development, tests); `smtp` sends with MailKit
/// (Hostinger: port 465, implicit TLS). The password comes from the environment, never the repo.
public sealed class EmailOptions
{
    public const string Section = "Store:Email";

    public string Mode { get; set; } = "sink";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 465;

    /// true = TLS on connect (465); false = STARTTLS when offered (587).
    public bool UseSsl { get; set; } = true;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = "store@ta3lom.sa";

    public string FromName { get; set; } = "متجر تعلّم";

    public string StaffAlerts { get; set; } = "store-alerts@ta3lom.sa";

    /// Sink folder; relative paths resolve against the content root.
    public string SinkFolder { get; set; } = "../../artifacts/mail";
}

/// A-8: Production sends staff mail over SMTP — `Mode = smtp` with host, user and password, or the host does not start.
internal sealed class EmailOptionsValidator(IHostEnvironment environment) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        if (!environment.IsProduction())
        {
            return ValidateOptionsResult.Success;
        }

        List<string> problems = [];
        if (!string.Equals(options.Mode, "smtp", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Store:Email:Mode must be smtp in Production");
        }

        foreach (var (key, value) in new[] { ("Store:Email:Host", options.Host), ("Store:Email:User", options.User), ("Store:Email:Password", options.Password) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{key} is required in Production");
            }
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

internal static class MimeBuilder
{
    public static MimeMessage Build(EmailOptions o, EmailMessage m)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.From));
        message.To.Add(MailboxAddress.Parse(m.To));
        message.Subject = m.Subject;
        var body = new BodyBuilder { TextBody = m.Body };
        foreach (var a in m.Attachments)
        {
            body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        }

        message.Body = body.ToMessageBody();
        return message;
    }
}

/// Development and tests: every e-mail becomes an `.eml` file (readable with `GET /dev/mail` or any mail client).
internal sealed class SinkEmailSender(IOptions<EmailOptions> options, IHostEnvironment environment, IClock clock) : IEmailSender, IDevMailInbox
{
    public string StaffAlertsAddress => options.Value.StaffAlerts;

    private string Folder => Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.SinkFolder));

    public async Task<DeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        Directory.CreateDirectory(Folder);
        var file = FormattableString.Invariant($"{clock.UtcNow:yyyyMMddHHmmssfff}-{Guid.CreateVersion7():N}.eml");
        await MimeBuilder.Build(options.Value, message).WriteToAsync(Path.Combine(Folder, file), ct);
        return DeliveryResult.Ok("sink:" + file);
    }

    public async Task<IReadOnlyList<DevMailMessage>> LatestAsync(int count, CancellationToken ct = default)
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        List<DevMailMessage> result = [];
        foreach (var path in Directory.GetFiles(Folder, "*.eml").OrderDescending(StringComparer.Ordinal).Take(Math.Clamp(count, 1, 200)))
        {
            using var mime = await MimeMessage.LoadAsync(path, ct);
            result.Add(new DevMailMessage(mime.Date, string.Join(", ", mime.To.Mailboxes.Select(a => a.Address)), mime.Subject ?? string.Empty,
                mime.TextBody ?? string.Empty, [.. mime.Attachments.Select(a => a.ContentDisposition?.FileName ?? "attachment")], Path.GetFileName(path)));
        }

        return result;
    }
}

/// Production: SMTP through MailKit. Provider failures become a failed result (the outbox retries).
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public string StaffAlertsAddress => options.Value.StaffAlerts;

    public async Task<DeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var o = options.Value;
        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(o.Host, o.Port, o.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct);
            if (o.User.Length > 0)
            {
                await client.AuthenticateAsync(o.User, o.Password, ct);
            }

            var response = await client.SendAsync(MimeBuilder.Build(o, message), ct);
            await QuitQuietlyAsync(client, ct);
            return DeliveryResult.Ok(response);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or AuthenticationException or SocketException or IOException
                                       or ServiceNotConnectedException)
        {
            return DeliveryResult.Failed(ex.Message);
        }
    }

    /// A-2: once the server accepted the message it is sent; a failing QUIT must not turn it into a retry (a second e-mail).
    private static async Task QuitQuietlyAsync(SmtpClient client, CancellationToken ct)
    {
        try
        {
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or SocketException or IOException or ServiceNotConnectedException)
        {
            // the message is already delivered
        }
    }
}
