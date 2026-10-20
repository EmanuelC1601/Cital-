using System.Data;
using Citalu.Application.Notifications;
using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Citalu.Infrastructure.Notifications;

/// <summary>Worker durable: el arranque y el barrido periódico recuperan pendientes y leases vencidos.</summary>
public sealed class OutboxProcessor(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxProcessor> logger) : BackgroundService
{
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
                var worked = await ProcessOne(stoppingToken);
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

    private async Task<bool> ProcessOne(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<OutboxQueue>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        // READPAST omite filas reclamadas por otros workers; el lease permite recuperación tras una caída.
        var claimedRows = await db.EmailOutbox.FromSqlInterpolated($"SELECT TOP (1) * FROM dbo.EmailOutbox WITH (UPDLOCK, READPAST, ROWLOCK) WHERE (Status = {(int)OutboxStatus.Pending} OR (Status = {(int)OutboxStatus.Processing} AND LockedUntilUtc < {now})) AND AvailableAtUtc <= {now} ORDER BY AvailableAtUtc, CreatedAtUtc")
            .ToListAsync(token);
        var item = claimedRows.SingleOrDefault();
        if (item is null) { await tx.CommitAsync(token); return false; }
        item.Claim(now.AddMinutes(2));
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);

        try
        {
            var payload = queue.Unprotect(item.ProtectedBody);
            // La cancelación/reprogramación puede ocurrir mientras el worker prepara el mensaje.
            // Revisa de nuevo la marca durable y la versión de cita justo antes de entregar al proveedor.
            var currentOutboxStatus = await db.EmailOutbox.AsNoTracking().Where(x => x.Id == item.Id)
                .Select(x => x.Status).SingleOrDefaultAsync(token);
            if (currentOutboxStatus != OutboxStatus.Processing)
            {
                item.Supersede(); await db.SaveChangesAsync(token); return true;
            }
            if (item.AppointmentId.HasValue)
            {
                var appointment = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.AppointmentId, token);
                var cancellationNotice = item.DeduplicationKey.Contains(":cancelación:", StringComparison.Ordinal);
                if (appointment is null || appointment.Status != AppointmentStatus.Confirmed && !(cancellationNotice && appointment.Status == AppointmentStatus.Cancelled) ||
                    item.AppointmentVersionUtc.HasValue && (appointment.UpdatedAtUtc ?? appointment.CreatedAtUtc) != item.AppointmentVersionUtc)
                {
                    item.Supersede(); await db.SaveChangesAsync(token); return true;
                }
            }
            await sender.SendAsync(new EmailMessage(payload.Recipient, payload.Subject, payload.Html, item.DeduplicationKey), token);
            item.Sent(clock.GetUtcNow()); await db.SaveChangesAsync(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Solo tipo de fallo: las excepciones de proveedores suelen contener direcciones o contenido.
            item.Retry(clock.GetUtcNow().AddSeconds(Math.Min(3600, Math.Pow(2, item.Attempts) * 15)), exception.GetType().Name);
            await db.SaveChangesAsync(token);
        }
        return true;
    }
}
