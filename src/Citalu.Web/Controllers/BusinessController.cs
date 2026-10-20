using Citalu.Application.Businesses;
using Citalu.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Citalu.Web.Controllers;

[Authorize, Route("negocio")]
public sealed class BusinessController(IBusinessStore store, CreateBusinessHandler createBusiness, UserManager<IdentityUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // El propietario se obtiene del principal autenticado; nunca del formulario o la URL.
        var ownerId = users.GetUserId(User)!;
        var business = await store.FindForOwnerAsync(ownerId, cancellationToken);
        return business is null
            ? View("Create", new BusinessInput())
            : View("Index", new BusinessSummary(business.Id, business.Name, business.Slug, business.TrialEndsAtUtc));
    }

    [HttpPost("crear"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BusinessInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Create", input);
        try
        {
            var ownerId = users.GetUserId(User)!;
            await createBusiness.HandleAsync(new CreateBusinessCommand(ownerId, input.Name), cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException)
        {
            return RedirectToAction(nameof(Index));
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(input.Name), exception.Message);
            return View("Create", input);
        }
    }
}
