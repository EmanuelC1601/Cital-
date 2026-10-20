namespace Citalu.Domain;

/// <summary>Trabajo durable; se cifra el contenido y se purga al terminar la retención.</summary>
public sealed class EmailOutboxMessage
{
    private EmailOutboxMessage() { }
    public EmailOutboxMessage(Guid? businessId, Guid? appointmentId, string recipient, string subject,
        string protectedBody, string deduplicationKey, DateTimeOffset availableAtUtc, DateTimeOffset? appointmentVersionUtc)
    {
        Id = Guid.NewGuid(); BusinessId = businessId; AppointmentId = appointmentId;
        Recipient = recipient; Subject = subject; ProtectedBody = protectedBody;
        DeduplicationKey = deduplicationKey; CreatedAtUtc = DateTimeOffset.UtcNow;
        AvailableAtUtc = availableAtUtc; AppointmentVersionUtc = appointmentVersionUtc;
        Status = OutboxStatus.Pending;
    }
    public Guid Id { get; private set; }
    public Guid? BusinessId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public string Recipient { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string ProtectedBody { get; private set; } = "";
    public string DeduplicationKey { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset AvailableAtUtc { get; private set; }
    public DateTimeOffset? AppointmentVersionUtc { get; private set; }
    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public string LastFailure { get; private set; } = "";
    public DateTimeOffset? SentAtUtc { get; private set; }
    public void Claim(DateTimeOffset until) { Status = OutboxStatus.Processing; LockedUntilUtc = until; Attempts++; }
    public void Retry(DateTimeOffset when, string reason) { Status = Attempts >= 8 ? OutboxStatus.Failed : OutboxStatus.Pending; AvailableAtUtc = when; LockedUntilUtc = null; LastFailure = reason[..Math.Min(reason.Length, 500)]; }
    public void Sent(DateTimeOffset at) { Status = OutboxStatus.Sent; SentAtUtc = at; LockedUntilUtc = null; ProtectedBody = ""; Recipient = ""; Subject = ""; }
    public void Supersede() { Status = OutboxStatus.Superseded; LockedUntilUtc = null; ProtectedBody = ""; Recipient = ""; Subject = ""; }
    public void RetryNow(DateTimeOffset now)
    {
        if (Status != OutboxStatus.Failed || string.IsNullOrWhiteSpace(ProtectedBody))
            throw new InvalidOperationException("El mensaje ya no se puede reintentar.");
        Status = OutboxStatus.Pending; Attempts = 0; AvailableAtUtc = now; LockedUntilUtc = null; LastFailure = "";
    }
}

public enum OutboxStatus { Pending, Processing, Sent, Failed, Superseded }
