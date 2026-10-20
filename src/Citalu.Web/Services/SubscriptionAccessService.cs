using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Citalu.Web.Services;

public sealed class SubscriptionSettings
{
    public string PlanCode { get; set; } = "monthly";
    public string PlanName { get; set; } = "Mensual";
    public decimal MonthlyPrice { get; set; } = 299m;
    public string Currency { get; set; } = "MXN";
    public int GraceDays { get; set; }
    public string TaxDisplay { get; set; } = "Impuestos sujetos a configuración";
    public string PaymentInstructions { get; set; } = "";
}

public sealed record SubscriptionState(bool CanBook, string Label, DateTimeOffset AccessUntilUtc,
    bool InGrace, decimal MonthlyPrice, string Currency, string PlanName, string TaxDisplay, int GraceDays);

/// <summary>Applies the same clock-controlled entitlement rule to every booking entry point.</summary>
public sealed class SubscriptionAccessService(ApplicationDbContext db, TimeProvider clock, IOptions<SubscriptionSettings> settings)
{
    public SubscriptionSettings Plan => settings.Value;

    public async Task<SubscriptionState> GetAsync(Guid businessId, CancellationToken token)
    {
        var subscription = await db.BusinessSubscriptions.SingleOrDefaultAsync(x => x.BusinessId == businessId, token)
            ?? throw new InvalidOperationException("El negocio no tiene suscripción migrada.");
        var periods = await db.SubscriptionPeriods.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == CoverageStatus.Granted)
            .OrderByDescending(x => x.EndsAtUtc).ToListAsync(token);
        var now = clock.GetUtcNow();
        var paid = periods.FirstOrDefault(x => x.StartsAtUtc <= now && now < x.EndsAtUtc);
        var until = periods.Count == 0 ? subscription.TrialEndsAtUtc :
            periods.Max(x => x.EndsAtUtc) > subscription.TrialEndsAtUtc ? periods.Max(x => x.EndsAtUtc) : subscription.TrialEndsAtUtc;
        var graceEnd = until.AddDays(Math.Clamp(Plan.GraceDays, 0, 90));
        var trial = now >= subscription.TrialStartsAtUtc && now < subscription.TrialEndsAtUtc;
        var inGrace = !trial && paid is null && now >= until && now < graceEnd;
        var canBook = trial || paid is not null || inGrace;
        var label = trial ? "Prueba gratuita vigente" : paid is not null ? "Periodo pagado vigente" :
            inGrace ? "Periodo de gracia" : "Acceso vencido";

        // State is refreshed on requests so expiry never depends on a scheduled task.
        var desired = trial ? SubscriptionStatus.Trial : paid is not null ? SubscriptionStatus.Paid : SubscriptionStatus.Expired;
        if (subscription.Status != desired)
        {
            if (desired == SubscriptionStatus.Trial) subscription.RestoreTrial();
            else if (desired == SubscriptionStatus.Paid) subscription.ActivatePaidPeriod();
            else subscription.MarkExpired();
            await db.SaveChangesAsync(token);
        }
        return new SubscriptionState(canBook, label, until, inGrace, Plan.MonthlyPrice, Plan.Currency,
            Plan.PlanName, Plan.TaxDisplay, Plan.GraceDays);
    }
}
