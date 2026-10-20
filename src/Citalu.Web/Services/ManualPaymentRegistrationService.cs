using System.Data;
using Citalu.Domain;
using Citalu.Infrastructure.Notifications;
using Citalu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Citalu.Web.Services;

/// <summary>Atomically appends a manual payment, grants one calendar month and invalidates stale expiry notices.</summary>
public sealed class ManualPaymentRegistrationService(ApplicationDbContext db, TimeProvider clock,
    IOptions<SubscriptionSettings> settings, OutboxQueue outbox)
{
    public async Task<SubscriptionPayment> RecordAsync(Guid businessId, decimal amount, string currency,
        string reference, DateTimeOffset paidAtUtc, string actorUserId, Guid requestKey, CancellationToken token)
    {
        var plan = settings.Value;
        if (amount <= 0 || decimal.Round(amount, 2) != amount || !string.Equals(currency, plan.Currency, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(reference) || reference.Length > 120 || string.IsNullOrWhiteSpace(actorUserId)
            || paidAtUtc > clock.GetUtcNow().AddMinutes(5)) throw new ArgumentException("Los datos del pago no son válidos.");
        var idempotency = requestKey.ToString("N");
        var existing = await db.SubscriptionPayments.SingleOrDefaultAsync(x => x.IdempotencyKey == idempotency, token);
        if (existing is not null) return existing;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var subscription = await db.BusinessSubscriptions.FromSqlInterpolated(
            $"SELECT * FROM dbo.BusinessSubscriptions WITH (UPDLOCK, HOLDLOCK) WHERE BusinessId = {businessId}").SingleOrDefaultAsync(token)
            ?? throw new KeyNotFoundException("No existe la suscripción del negocio.");
        existing = await db.SubscriptionPayments.SingleOrDefaultAsync(x => x.IdempotencyKey == idempotency, token);
        if (existing is not null) { await tx.CommitAsync(token); return existing; }

        var now = clock.GetUtcNow();
        var lastPeriodEnd = await db.SubscriptionPeriods.Where(x => x.SubscriptionId == subscription.Id && x.Status == CoverageStatus.Granted)
            .Select(x => (DateTimeOffset?)x.EndsAtUtc).MaxAsync(token);
        // Advance payment follows the current trial/coverage. Late payment begins now and never backfills expired days.
        var start = lastPeriodEnd.HasValue && lastPeriodEnd.Value > now ? lastPeriodEnd.Value : now;
        if (subscription.TrialEndsAtUtc > start) start = subscription.TrialEndsAtUtc;
        var end = start.AddMonths(1);
        var period = new SubscriptionPeriod(subscription.Id, start, end, amount, plan.Currency, plan.PlanCode, actorUserId, now);
        var payment = new SubscriptionPayment(subscription.Id, period.Id, amount, plan.Currency, reference, paidAtUtc,
            now, actorUserId, idempotency);
        db.SubscriptionPeriods.Add(period);
        db.SubscriptionPayments.Add(payment);
        db.SubscriptionAudits.Add(new SubscriptionAudit(subscription.Id, "Pago registrado", actorUserId,
            $"Periodo {start:O} a {end:O}; {amount:0.00} {plan.Currency}; referencia {reference.Trim()}.", now));
        if (start <= now && now < end) subscription.ActivatePaidPeriod();
        await outbox.SupersedeSubscriptionNoticesAsync(businessId, token);
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return payment;
    }
}
