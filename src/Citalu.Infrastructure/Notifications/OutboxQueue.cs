using System.Text.Json;
using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>Invalida avisos aún no entregados cuando cambia la cita y borra sus datos sensibles.</summary>
    public Task<int> SupersedeAppointmentMessagesAsync(Guid appointmentId, CancellationToken token) => db.EmailOutbox
        .Where(x => x.AppointmentId == appointmentId && (x.Status == OutboxStatus.Pending || x.Status == OutboxStatus.Processing))
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, OutboxStatus.Superseded)
            .SetProperty(x => x.ProtectedBody, "").SetProperty(x => x.Recipient, "").SetProperty(x => x.Subject, "")
            .SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null).SetProperty(x => x.LeaseId, (Guid?)null), token);

    public sealed record CapturedPayload(string Recipient, string Subject, string Html);
}
