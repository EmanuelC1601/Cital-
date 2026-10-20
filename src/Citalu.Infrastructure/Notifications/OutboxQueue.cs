using System.Text.Json;
using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace Citalu.Infrastructure.Notifications;

/// <summary>Agrega trabajos al mismo DbContext/transacción que la operación de negocio.</summary>
public sealed class OutboxQueue(ApplicationDbContext db, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("Citalu.EmailOutbox.Payload.v1");

    public void Enqueue(Guid? businessId, Guid? appointmentId, string recipient, string subject, string html,
        string deduplicationKey, DateTimeOffset availableAtUtc, DateTimeOffset? appointmentVersionUtc = null)
    {
        if (string.IsNullOrWhiteSpace(recipient) || recipient.Length > 254) return;
        // La clave protege los reintentos/idempotencia de negocio y evita insertar el mismo correo dos veces.
        if (db.EmailOutbox.Local.Any(x => x.DeduplicationKey == deduplicationKey) ||
            db.EmailOutbox.Any(x => x.DeduplicationKey == deduplicationKey)) return;
        var payload = JsonSerializer.Serialize(new CapturedPayload(recipient, subject, html));
        db.EmailOutbox.Add(new EmailOutboxMessage(businessId, appointmentId, "", "Notificación de Citalú",
            protector.Protect(payload), deduplicationKey, availableAtUtc, appointmentVersionUtc));
    }

    public CapturedPayload Unprotect(string encrypted) => JsonSerializer.Deserialize<CapturedPayload>(protector.Unprotect(encrypted))!;
    public Task SaveAsync(CancellationToken token) => db.SaveChangesAsync(token);
    public sealed record CapturedPayload(string Recipient, string Subject, string Html);
}
