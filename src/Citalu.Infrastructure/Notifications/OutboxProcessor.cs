using System.Data;
using Citalu.Application.Notifications;
using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Citalu.Infrastructure.Notifications;

/// <summary>Worker durable: SQL guarda los trabajos y leases; el sondeo recupera trabajos tras reinicios.</summary>
public sealed class OutboxProcessor(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRetentionSweep = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (clock.GetUtcNow() >= nextRetentionSweep)
                {
                    await PurgeExpiredPayloads(stoppingToken);
                    nextRetentionSweep = clock.GetUtcNow().AddHours(1);
                }
                var worked = await ProcessOneAsync(stoppingToken);
                if (!worked) await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError("Outbox worker falló y volverá a intentar: {FailureType}", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PurgeExpiredPayloads(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.EmailOutbox.Where(x => x.CreatedAtUtc < clock.GetUtcNow().AddDays(-30) && x.ProtectedBody != "")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProtectedBody, "")
                .SetProperty(x => x.Recipient, "").SetProperty(x => x.Subject, ""), token);
    }

    /// <summary>Una unidad de trabajo, expuesta para las pruebas SQL de integración.</summary>
    internal async Task<bool> ProcessOneAsync(CancellationToken token, string? deduplicationKeyPrefix = null)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<OutboxQueue>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var matchAllKeys = deduplicationKeyPrefix is null;
        var keyPattern = deduplicationKeyPrefix + "%";
        var claimedRows = await db.EmailOutbox.FromSqlInterpolated($"SELECT TOP (1) * FROM dbo.EmailOutbox WITH (UPDLOCK, READPAST, ROWLOCK) WHERE (Status = {(int)OutboxStatus.Pending} OR (Status = {(int)OutboxStatus.Processing} AND LockedUntilUtc < {now})) AND AvailableAtUtc <= {now} AND ({matchAllKeys} = 1 OR DeduplicationKey LIKE {keyPattern}) ORDER BY AvailableAtUtc, CreatedAtUtc")
            .ToListAsync(token);
        var item = claimedRows.SingleOrDefault();
        if (item is null) { await tx.CommitAsync(token); return false; }
        item.Claim(now.Add(LeaseDuration));
        var leaseId = item.LeaseId!.Value;
        var id = item.Id;
        var appointmentId = item.AppointmentId;
        var businessId = item.BusinessId;
        var version = item.AppointmentVersionUtc;
        var deduplicationKey = item.DeduplicationKey;
        var protectedBody = item.ProtectedBody;
        var attempts = item.Attempts;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);

        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var renewal = RenewLeaseUntilStoppedAsync(id, leaseId, sendCts, token);
        try
        {
            if (!await IsCurrentAsync(id, leaseId, appointmentId, version, deduplicationKey, token))
            {
                await CompleteSupersededAsync(id, leaseId, token);
                return true;
            }
            var payload = queue.Unprotect(protectedBody);
            await sender.SendAsync(new EmailMessage(payload.Recipient, payload.Subject, payload.Html, deduplicationKey), sendCts.Token);
            sendCts.Cancel();
            await renewal;
            // Fencing: an expired owner cannot overwrite the state written by a worker that reclaimed the row.
            await CompleteSentAsync(id, leaseId, clock.GetUtcNow(), token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // Lost lease: the new owner controls the row. Never record this owner's result.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            sendCts.Cancel();
            await renewal;
            // Only the lease owner may record a failure; exception text may contain PII, so store type only.
            await CompleteFailureAsync(id, leaseId, clock.GetUtcNow().AddSeconds(Math.Min(3600, Math.Pow(2, attempts) * 15)), exception.GetType().Name, token);
        }
        finally
        {
            sendCts.Cancel();
        }
        return true;
    }

    private async Task RenewLeaseUntilStoppedAsync(Guid id, Guid leaseId, CancellationTokenSource sendCts, CancellationToken stoppingToken)
    {
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, sendCts.Token);
        while (!renewalCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LeaseRenewalInterval, clock, renewalCts.Token);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var renewed = await db.EmailOutbox.Where(x => x.Id == id && x.Status == OutboxStatus.Processing && x.LeaseId == leaseId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LockedUntilUtc, clock.GetUtcNow().Add(LeaseDuration)), renewalCts.Token);
                if (renewed == 1) continue;

                sendCts.Cancel();
                return;
            }
            catch (OperationCanceledException) when (renewalCts.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                // If ownership cannot be confirmed in SQL, stop delivery and let the durable lease expire/recover.
                logger.LogWarning("No se pudo renovar un lease del outbox: {FailureType}", exception.GetType().Name);
                sendCts.Cancel();
                return;
            }
        }
    }

    private async Task<bool> IsCurrentAsync(Guid id, Guid leaseId, Guid? appointmentId, DateTimeOffset? version, string key, CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = await db.EmailOutbox.AsNoTracking().Where(x => x.Id == id && x.LeaseId == leaseId && x.Status == OutboxStatus.Processing)
            .Select(x => new { x.BusinessId, x.Status }).SingleOrDefaultAsync(token);
        if (item is null) return false;
        if (!appointmentId.HasValue)
        {
            if (!key.StartsWith("subscription:expiry:", StringComparison.Ordinal)) return true;
            var parts = key.Split(':');
            if (parts.Length != 5 || !Guid.TryParseExact(parts[2], "N", out var businessId) || !long.TryParse(parts[3], out var expectedTicks)
                || item.BusinessId != businessId) return false;
            var subscription = await db.BusinessSubscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessId == businessId, token);
            if (subscription is null) return false;
            var paidEnd = await db.SubscriptionPeriods.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id && x.Status == CoverageStatus.Granted)
                .Select(x => (DateTimeOffset?)x.EndsAtUtc).MaxAsync(token);
            var effectiveEnd = paidEnd.HasValue && paidEnd.Value > subscription.TrialEndsAtUtc ? paidEnd.Value : subscription.TrialEndsAtUtc;
            return effectiveEnd.UtcTicks == expectedTicks;
        }
        var appointment = await db.Appointments.AsNoTracking().Where(x => x.Id == appointmentId && x.BusinessId == item.BusinessId)
            .Select(x => new { x.Status, x.UpdatedAtUtc, x.CreatedAtUtc }).SingleOrDefaultAsync(token);
        var cancellationNotice = key.Contains(":cancelación:", StringComparison.Ordinal);
        return appointment is not null &&
               (appointment.Status == AppointmentStatus.Confirmed || cancellationNotice && appointment.Status == AppointmentStatus.Cancelled) &&
               (!version.HasValue || (appointment.UpdatedAtUtc ?? appointment.CreatedAtUtc) == version);
    }

    private async Task<int> CompleteSentAsync(Guid id, Guid leaseId, DateTimeOffset at, CancellationToken token)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.EmailOutbox.Where(x => x.Id == id && x.LeaseId == leaseId && x.Status == OutboxStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OutboxStatus.Sent).SetProperty(x => x.SentAtUtc, at)
                .SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null).SetProperty(x => x.LeaseId, (Guid?)null)
                .SetProperty(x => x.ProtectedBody, "").SetProperty(x => x.Recipient, "").SetProperty(x => x.Subject, ""), token);
    }

    private async Task<int> CompleteSupersededAsync(Guid id, Guid leaseId, CancellationToken token)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.EmailOutbox.Where(x => x.Id == id && x.LeaseId == leaseId && x.Status == OutboxStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OutboxStatus.Superseded).SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.ProtectedBody, "")
                .SetProperty(x => x.Recipient, "").SetProperty(x => x.Subject, ""), token);
    }

    private async Task<int> CompleteFailureAsync(Guid id, Guid leaseId, DateTimeOffset retryAt, string failure, CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.EmailOutbox.Where(x => x.Id == id && x.LeaseId == leaseId && x.Status == OutboxStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, x => x.Attempts >= 8 ? OutboxStatus.Failed : OutboxStatus.Pending)
                .SetProperty(x => x.AvailableAtUtc, retryAt).SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LastFailure, failure), token);
    }

}
