using Citalu.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Citalu.Infrastructure.Notifications;
using Citalu.Infrastructure.Persistence;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Citalu.Web.Controllers;

[Route("cuenta")]
public sealed class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signInManager, OutboxQueue outbox,
    TimeProvider clock, ApplicationDbContext db, IConfiguration configuration) : Controller
{
    [HttpGet("registro"), AllowAnonymous]
    public IActionResult Register() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Business") : View(new RegisterInput());

    [HttpPost("registro"), AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (!ModelState.IsValid) return View(input);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, HttpContext.RequestAborted);
        var user = new IdentityUser { UserName = input.Email.Trim(), Email = input.Email.Trim() };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            TempData["AccountMessage"] = "Si los datos permiten crear una cuenta, recibirás instrucciones para continuar.";
            return RedirectToAction(nameof(Register));
        }

        await QueueConfirmationAsync(user);
        await transaction.CommitAsync();
        TempData["AccountMessage"] = "Si la dirección puede usarse, recibirás instrucciones para confirmar tu correo.";
        return RedirectToAction(nameof(Register));
    }

    [HttpGet("iniciar-sesion"), AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Business") : View(new LoginInput());
    }

    [HttpPost("iniciar-sesion"), AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(input);

        var result = await signInManager.PasswordSignInAsync(input.Email.Trim(), input.Password, input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var user = await users.FindByEmailAsync(input.Email.Trim());
            if (user is not null && !await users.IsEmailConfirmedAsync(user))
            {
                TempData["AccountMessage"] = "Tu cuenta sigue activa. Confirma el correo cuando puedas para validar que puedes recibir avisos.";
                TempData["NeedsConfirmation"] = true;
            }
            return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Business");
        }
        ModelState.AddModelError(string.Empty, "No se pudo iniciar sesión. Revisa tus datos e inténtalo de nuevo.");
        return View(input);
    }

    [HttpPost("cerrar-sesion"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("confirmar"), AllowAnonymous]
    public async Task<IActionResult> Confirm(string userId, string code)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || string.IsNullOrWhiteSpace(code)) return View("Message");
        var result = await users.ConfirmEmailAsync(user, code);
        ViewData["Message"] = result.Succeeded ? "Correo confirmado. Ya puedes usar tu cuenta." : "El enlace no es válido o venció. Solicita uno nuevo.";
        return View("Message");
    }

    [HttpGet("confirmar/reenviar"), AllowAnonymous]
    public IActionResult ResendConfirmation() => View("EmailInput", new EmailInput());

    [HttpPost("confirmar/reenviar"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("account-recovery")]
    public async Task<IActionResult> ResendConfirmation(EmailInput input)
    {
        if (ModelState.IsValid)
        {
            var user = await users.FindByEmailAsync(input.Email.Trim());
            if (user is not null && !await users.IsEmailConfirmedAsync(user)) await QueueConfirmationAsync(user);
        }
        TempData["AccountMessage"] = "Si existe una cuenta pendiente de confirmar para ese correo, enviaremos instrucciones.";
        return RedirectToAction(nameof(ResendConfirmation));
    }

    [HttpGet("recuperar"), AllowAnonymous]
    public IActionResult ForgotPassword() => View("EmailInput", new EmailInput());

    [HttpPost("recuperar"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("account-recovery")]
    public async Task<IActionResult> ForgotPassword(EmailInput input)
    {
        if (ModelState.IsValid)
        {
            var user = await users.FindByEmailAsync(input.Email.Trim());
            if (user is not null && await users.IsEmailConfirmedAsync(user))
            {
                var code = await users.GeneratePasswordResetTokenAsync(user);
                var link = BuildAccountLink("restablecer", new Dictionary<string, string?> { ["userId"] = user.Id, ["code"] = code });
                outbox.Enqueue(null, null, user.Email!, "Recupera tu acceso a Citalú",
                    $"<p>Para cambiar tu contraseña, <a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">abre este enlace</a>. Si no lo solicitaste, ignora este mensaje.</p>",
                    $"account:password-reset:{user.Id}:{TokenFingerprint(code)}", clock.GetUtcNow());
                await outbox.SaveAsync(HttpContext.RequestAborted);
            }
        }
        TempData["AccountMessage"] = "Si la cuenta existe y puede recuperarse, recibirás instrucciones por correo.";
        return RedirectToAction(nameof(ForgotPassword));
    }

    [HttpGet("restablecer"), AllowAnonymous]
    public IActionResult ResetPassword(string? userId, string? code) => View(new ResetPasswordInput { UserId = userId ?? "", Code = code ?? "" });

    [HttpPost("restablecer"), AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByIdAsync(input.UserId);
        if (user is null) { ModelState.AddModelError(string.Empty, "No se pudo restablecer el acceso. Solicita un enlace nuevo."); return View(input); }
        var result = await users.ResetPasswordAsync(user, input.Code, input.Password);
        if (result.Succeeded) { TempData["AccountMessage"] = "Contraseña actualizada. Inicia sesión."; return RedirectToAction(nameof(Login)); }
        ModelState.AddModelError(string.Empty, "El enlace no es válido o venció. Solicita uno nuevo.");
        return View(input);
    }

    private async Task QueueConfirmationAsync(IdentityUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return;
        var code = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = BuildAccountLink("confirmar", new Dictionary<string, string?> { ["userId"] = user.Id, ["code"] = code });
        outbox.Enqueue(null, null, user.Email, "Confirma tu correo de Citalú",
            $"<p>Confirma tu correo para verificar que puedes recibir avisos: <a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">confirmar correo</a>.</p>",
            $"account:email-confirm:{user.Id}:{TokenFingerprint(code)}", clock.GetUtcNow());
        await outbox.SaveAsync(HttpContext.RequestAborted);
    }

    private string BuildAccountLink(string path, Dictionary<string, string?> query)
    {
        var baseUrl = configuration["Application:PublicBaseUrl"]?.TrimEnd('/') ?? "http://localhost:5000";
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString($"{baseUrl}/cuenta/{path}", query);
    }

    private static string TokenFingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
