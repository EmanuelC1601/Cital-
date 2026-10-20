namespace Citalu.Domain;

/// <summary>Billing state is separate from appointments and preserves the original trial dates.</summary>
public sealed class BusinessSubscription
{
    private BusinessSubscription() { }
    public BusinessSubscription(Guid businessId, DateTimeOffset trialStartsAtUtc, DateTimeOffset trialEndsAtUtc, string planCode)
    {
        if (trialEndsAtUtc <= trialStartsAtUtc) throw new ArgumentException("El periodo de prueba no es válido.");
        Id = Guid.NewGuid(); BusinessId = businessId; TrialStartsAtUtc = trialStartsAtUtc.ToUniversalTime();
        TrialEndsAtUtc = trialEndsAtUtc.ToUniversalTime(); PlanCode = planCode;
        Status = SubscriptionStatus.Trial; RenewalCancelled = false; CreatedAtUtc = trialStartsAtUtc.ToUniversalTime();
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public DateTimeOffset TrialStartsAtUtc { get; private set; }
    public DateTimeOffset TrialEndsAtUtc { get; private set; }
    public string PlanCode { get; private set; } = "monthly";
    public SubscriptionStatus Status { get; private set; }
    public bool RenewalCancelled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public void ActivatePaidPeriod() { Status = SubscriptionStatus.Paid; RenewalCancelled = false; }
    public void CancelRenewal() { RenewalCancelled = true; }
    public void ResumeRenewal() { RenewalCancelled = false; }
    public void MarkExpired() { Status = SubscriptionStatus.Expired; }
    public void RestoreTrial() { Status = SubscriptionStatus.Trial; }
}

public enum SubscriptionStatus { Trial = 0, Paid = 1, Expired = 2 }

/// <summary>Coverage intervals are UTC half-open [start,end); history is retained if explicitly revoked.</summary>
public sealed class SubscriptionPeriod
{
    private SubscriptionPeriod() { }
    public SubscriptionPeriod(Guid subscriptionId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc,
        decimal amount, string currency, string planCode, string grantedByUserId, DateTimeOffset grantedAtUtc)
    {
        if (endsAtUtc <= startsAtUtc || amount < 0 || decimal.Round(amount, 2) != amount) throw new ArgumentException("El periodo concedido no es válido.");
        Id = Guid.NewGuid(); SubscriptionId = subscriptionId; StartsAtUtc = startsAtUtc.ToUniversalTime();
        EndsAtUtc = endsAtUtc.ToUniversalTime(); Amount = amount; Currency = currency; PlanCode = planCode;
        GrantedByUserId = grantedByUserId; GrantedAtUtc = grantedAtUtc.ToUniversalTime(); Status = CoverageStatus.Granted;
    }
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "MXN";
    public string PlanCode { get; private set; } = "monthly";
    public string GrantedByUserId { get; private set; } = "";
    public DateTimeOffset GrantedAtUtc { get; private set; }
    public CoverageStatus Status { get; private set; }
    public string RevocationReason { get; private set; } = "";
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public void Revoke(string reason, DateTimeOffset atUtc)
    {
        if (Status == CoverageStatus.Revoked || string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("El periodo no puede corregirse de esa forma.");
        Status = CoverageStatus.Revoked; RevocationReason = reason.Trim(); RevokedAtUtc = atUtc.ToUniversalTime();
    }
}

public enum CoverageStatus { Granted = 0, Revoked = 1 }

/// <summary>Append-only manual payment and compensating reversal records; never delete payment history.</summary>
public sealed class SubscriptionPayment
{
    private SubscriptionPayment() { }
    public SubscriptionPayment(Guid subscriptionId, Guid periodId, decimal amount, string currency, string reference,
        DateTimeOffset paidAtUtc, DateTimeOffset recordedAtUtc, string recordedByUserId, string idempotencyKey,
        Guid? reversesPaymentId = null, string correctionReason = "")
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount || currency.Length != 3 || string.IsNullOrWhiteSpace(reference)
            || string.IsNullOrWhiteSpace(recordedByUserId) || string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Los datos del registro de pago no son válidos.");
        if (reversesPaymentId.HasValue && string.IsNullOrWhiteSpace(correctionReason)) throw new ArgumentException("Indica el motivo de la corrección.");
        Id = Guid.NewGuid(); SubscriptionId = subscriptionId; PeriodId = periodId; Amount = amount; Currency = currency.ToUpperInvariant();
        Reference = reference.Trim(); PaidAtUtc = paidAtUtc.ToUniversalTime(); RecordedAtUtc = recordedAtUtc.ToUniversalTime();
        RecordedByUserId = recordedByUserId; IdempotencyKey = idempotencyKey; ReversesPaymentId = reversesPaymentId;
        CorrectionReason = correctionReason.Trim();
    }
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid PeriodId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "MXN";
    public string Reference { get; private set; } = "";
    public DateTimeOffset PaidAtUtc { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public string RecordedByUserId { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    public Guid? ReversesPaymentId { get; private set; }
    public string CorrectionReason { get; private set; } = "";
}

public sealed class SubscriptionAudit
{
    private SubscriptionAudit() { }
    public SubscriptionAudit(Guid subscriptionId, string action, string actorUserId, string detail, DateTimeOffset atUtc)
    { Id = Guid.NewGuid(); SubscriptionId = subscriptionId; Action = action; ActorUserId = actorUserId; Detail = detail; AtUtc = atUtc.ToUniversalTime(); }
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string Action { get; private set; } = "";
    public string ActorUserId { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public DateTimeOffset AtUtc { get; private set; }
}
