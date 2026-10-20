using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Citalu.Infrastructure.Notifications;

/// <summary>Durably enqueues 7-day and 1-day expiration notices; SQL outbox remains the delivery guarantee.</summary>
public sealed class SubscriptionExpiryNotifier(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<SubscriptionExpiryNotifier> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScheduleDueNoticesAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning("No se pudo programar avisos de vencimiento: {FailureType}", exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromHours(1), clock, stoppingToken);
        }
    }

    internal async Task ScheduleDueNoticesAsync(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var queue = scope.ServiceProvider.GetRequiredService<OutboxQueue>();
        var now = clock.GetUtcNow();
        var subscriptions = await db.BusinessSubscriptions.AsNoTracking().ToListAsync(token);
        foreach (var subscription in subscriptions)
        {
            var paidEnds = await db.SubscriptionPeriods.AsNoTracking()
                .Where(x => x.SubscriptionId == subscription.Id && x.Status == CoverageStatus.Granted)
                .Select(x => (DateTimeOffset?)x.EndsAtUtc).ToListAsync(token);
            var end = paidEnds.Where(x => x.HasValue).Select(x => x!.Value).Append(subscription.TrialEndsAtUtc).Max();
            foreach (var leadDays in new[] { 7, 1 })
            {
                var noticeAt = end.AddDays(-leadDays);
                if (now < noticeAt || now >= end) continue;
                var business = await db.Businesses.SingleOrDefaultAsync(x => x.Id == subscription.BusinessId, token);
                if (business is null) continue;
                var owner = await users.FindByIdAsync(business.OwnerUserId);
                if (owner?.EmailConfirmed != true || string.IsNullOrWhiteSpace(owner.Email)) continue;
                var key = $"subscription:expiry:{business.Id:N}:{end.UtcTicks}:{leadDays}";
                var atText = end.ToString("d 'de' MMMM 'de' yyyy", System.Globalization.CultureInfo.GetCultureInfo("es-MX"));
                queue.Enqueue(business.Id, null, owner.Email, $"Tu acceso a Citalú vence el {atText}",
                    $"<p>El acceso a tu negocio vence el {atText}.</p><p>Consulta opciones para registrar un pago en tu panel. El pago manual solo lo confirma un administrador; no se cobran importes automáticamente.</p>",
                    key, now, end);
            }
        }
        await queue.SaveAsync(token);
    }
}
