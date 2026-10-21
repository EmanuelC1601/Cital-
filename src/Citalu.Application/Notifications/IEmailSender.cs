namespace Citalu.Application.Notifications;

/// <summary>Transporte neutral. Las implementaciones no deben registrar destinatarios ni contenido.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string Recipient, string Subject, string HtmlBody, string IdempotencyKey);
