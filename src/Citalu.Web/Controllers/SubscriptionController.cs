using System.Data;
using Citalu.Domain;
using Citalu.Infrastructure.Notifications;
using Citalu.Infrastructure.Persistence;
using Citalu.Web.Models;
using Citalu.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Citalu.Web.Controllers;

[Authorize, Route("negocio/suscripcion")]
public sealed class SubscriptionController(ApplicationDbContext db, UserManager<IdentityUser> users,
    SubscriptionAccessService access, IOptions<SubscriptionSettings> options, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        var ownerId = users.GetUserId(User);
        var business = await db.Businesses.SingleOrDefaultAsync(x => x.OwnerUserId == ownerId, token);
        if (business is null) return RedirectToAction("Index", "Business");
        var sub = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == business.Id, token);
        var state = await access.GetAsync(business.Id, token);
        return View(new SubscriptionOwnerPage(business.Name, state, sub,
            await db.SubscriptionPeriods.Where(x => x.SubscriptionId == sub.Id).OrderByDescending(x => x.StartsAtUtc).ToListAsync(token),
            await db.SubscriptionPayments.Where(x => x.SubscriptionId == sub.Id).OrderByDescending(x => x.RecordedAtUtc).ToListAsync(token),
            await db.SubscriptionAudits.Where(x => x.SubscriptionId == sub.Id).OrderByDescending(x => x.AtUtc).Take(100).ToListAsync(token),
            options.Value.PaymentInstructions, Guid.NewGuid()));
    }

    [HttpPost("cancelar-renovacion"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRenewal(CancellationToken token)
    {
        var ownerId = users.GetUserId(User);
        var business = await db.Businesses.SingleOrDefaultAsync(x => x.OwnerUserId == ownerId, token);
        if (business is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var sub = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == business.Id, token);
        sub.CancelRenewal();
        db.SubscriptionAudits.Add(new SubscriptionAudit(sub.Id, "Renovación cancelada", ownerId!, "No se generarán nuevos periodos automáticamente.", clock.GetUtcNow()));
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        TempData["Notice"] = "Se canceló la renovación futura. Conservas el acceso hasta el fin del periodo vigente.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("reactivar-renovacion"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ResumeRenewal(CancellationToken token)
    {
        var ownerId = users.GetUserId(User);
        var business = await db.Businesses.SingleOrDefaultAsync(x => x.OwnerUserId == ownerId, token);
        if (business is null) return NotFound();
        var sub = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == business.Id, token);
        sub.ResumeRenewal();
        db.SubscriptionAudits.Add(new SubscriptionAudit(sub.Id, "Renovación habilitada", ownerId!, "Se habilitó solicitar periodos futuros.", clock.GetUtcNow()));
        await db.SaveChangesAsync(token);
        TempData["Notice"] = "La renovación quedó habilitada. No se hará ningún cobro automático.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize(Roles = PlatformSubscriptionAdminController.AdminRole), Route("plataforma/suscripciones")]
public sealed class PlatformSubscriptionAdminController(ApplicationDbContext db, UserManager<IdentityUser> users,
    SubscriptionAccessService access, IOptions<SubscriptionSettings> options, TimeProvider clock,
    ManualPaymentRegistrationService paymentRegistration,
    OutboxQueue outbox) : Controller
{
    public const string AdminRole = "PlatformAdmin";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        var plan = options.Value;
        var businesses = await db.Businesses.OrderBy(x => x.Name).ToListAsync(token);
        var rows = new List<PlatformSubscriptionRow>();
        foreach (var business in businesses)
        {
            var sub = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == business.Id, token);
            var ownerEmail = await users.Users.Where(x => x.Id == business.OwnerUserId).Select(x => x.Email).SingleOrDefaultAsync(token) ?? "";
            rows.Add(new PlatformSubscriptionRow(business.Id, business.Name, business.Slug, ownerEmail,
                await access.GetAsync(business.Id, token), sub,
                await db.SubscriptionPeriods.Where(x => x.SubscriptionId == sub.Id).OrderByDescending(x => x.StartsAtUtc).ToListAsync(token),
                await db.SubscriptionPayments.Where(x => x.SubscriptionId == sub.Id).OrderByDescending(x => x.RecordedAtUtc).ToListAsync(token)));
        }
        return View(new PlatformSubscriptionAdminPage(rows, plan.PlanName, plan.MonthlyPrice, plan.Currency, plan.TaxDisplay, Guid.NewGuid()));
    }

    [HttpPost("registrar-pago"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(ManualPaymentInput input, CancellationToken token)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Revisa los datos del pago."; return RedirectToAction(nameof(Index)); }
        var plan = options.Value;
        if (!string.Equals(input.Currency, plan.Currency, StringComparison.OrdinalIgnoreCase) || input.PaidAtUtc > clock.GetUtcNow().AddMinutes(5))
        { TempData["Error"] = "La moneda debe coincidir con el plan y la fecha no puede estar en el futuro."; return RedirectToAction(nameof(Index)); }
        try
        {
            await paymentRegistration.RecordAsync(input.BusinessId, input.Amount, input.Currency, input.Reference,
                input.PaidAtUtc, users.GetUserId(User)!, input.RequestKey, token);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException) { TempData["Error"] = "La moneda debe coincidir con el plan, el importe debe ser positivo y la fecha no puede ser futura."; return RedirectToAction(nameof(Index)); }
        TempData["Notice"] = "Pago registrado y periodo concedido. El acceso ya puede reservar; el registro quedó en auditoría.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("corregir-pago"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrectPayment(CorrectionInput input, CancellationToken token)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Indica un motivo para corregir el pago."; return RedirectToAction(nameof(Index)); }
        var original = await db.SubscriptionPayments.SingleOrDefaultAsync(x => x.Id == input.PaymentId && x.ReversesPaymentId == null, token);
        if (original is null) return NotFound();
        if (await db.SubscriptionPayments.AnyAsync(x => x.ReversesPaymentId == original.Id, token))
        { TempData["Notice"] = "Ese pago ya tiene una corrección registrada."; return RedirectToAction(nameof(Index)); }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var period = await db.SubscriptionPeriods.SingleAsync(x => x.Id == original.PeriodId, token);
        var sub = await db.BusinessSubscriptions.SingleAsync(x => x.Id == original.SubscriptionId, token);
        period.Revoke(input.Reason, clock.GetUtcNow());
        db.SubscriptionPayments.Add(new SubscriptionPayment(original.SubscriptionId, original.PeriodId, original.Amount,
            original.Currency, $"Reverso: {original.Reference}", clock.GetUtcNow(), clock.GetUtcNow(), users.GetUserId(User)!,
            input.RequestKey.ToString("N"), original.Id, input.Reason));
        db.SubscriptionAudits.Add(new SubscriptionAudit(sub.Id, "Pago corregido", users.GetUserId(User)!,
            $"Pago {original.Id} y periodo {period.Id} revocados explícitamente: {input.Reason.Trim()}.", clock.GetUtcNow()));
        await outbox.SupersedeSubscriptionNoticesAsync((await db.BusinessSubscriptions.Where(x => x.Id == sub.Id).Select(x => x.BusinessId).SingleAsync(token)), token);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        TempData["Notice"] = "La corrección quedó registrada; el pago y el periodo originales se conservan en el historial.";
        return RedirectToAction(nameof(Index));
    }
}
