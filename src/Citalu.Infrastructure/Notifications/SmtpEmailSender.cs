using System.Net;
using System.Net.Mail;
using Citalu.Application.Notifications;
using Microsoft.Extensions.Configuration;

namespace Citalu.Infrastructure.Notifications;

/// <summary>Transporte SMTP opcional. La entrega falla de forma recuperable mientras falte configuración.</summary>
public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var host = configuration["Email:Smtp:Host"];
        var from = configuration["Email:Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("SMTP no está configurado.");
        var port = int.TryParse(configuration["Email:Smtp:Port"], out var parsedPort) ? parsedPort : 587;
        using var mail = new MailMessage(from, message.Recipient, message.Subject, message.HtmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = !bool.TryParse(configuration["Email:Smtp:EnableSsl"], out var ssl) || ssl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        var user = configuration["Email:Smtp:Username"];
        var password = configuration["Email:Smtp:Password"];
        if (!string.IsNullOrWhiteSpace(user)) client.Credentials = new NetworkCredential(user, password);
        await client.SendMailAsync(mail, cancellationToken);
    }
}
