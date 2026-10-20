using Citalu.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Citalu.Web.Controllers;

[Route("cuenta")]
public sealed class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signInManager) : Controller
{
    [HttpGet("registro"), AllowAnonymous]
    public IActionResult Register() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Business") : View(new RegisterInput());

    [HttpPost("registro"), AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = new IdentityUser { UserName = input.Email.Trim(), Email = input.Email.Trim() };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(input);
        }

        // En producción esta sesión se habilitará después de implementar confirmación de correo.
        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Business");
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
        if (result.Succeeded) return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Business");
        ModelState.AddModelError(string.Empty, "No se pudo iniciar sesión. Revisa tus datos e inténtalo de nuevo.");
        return View(input);
    }

    [HttpPost("cerrar-sesion"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}
