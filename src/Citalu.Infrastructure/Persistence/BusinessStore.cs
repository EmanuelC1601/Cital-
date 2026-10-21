using Citalu.Application.Businesses;
using Citalu.Domain;
using Microsoft.EntityFrameworkCore;

namespace Citalu.Infrastructure.Persistence;

/// <summary>Las consultas de propietario siempre incluyen el usuario autenticado como filtro explícito.</summary>
public sealed class BusinessStore(ApplicationDbContext db) : IBusinessStore
{
    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        db.Businesses.AnyAsync(business => business.Slug == slug, cancellationToken);

    public Task<Business?> FindForOwnerAsync(string ownerUserId, CancellationToken cancellationToken) =>
        db.Businesses.SingleOrDefaultAsync(business => business.OwnerUserId == ownerUserId, cancellationToken);

    public async Task AddAsync(Business business, CancellationToken cancellationToken)
    {
        db.Businesses.Add(business);
        db.BusinessSubscriptions.Add(new BusinessSubscription(business.Id, business.TrialStartedAtUtc, business.TrialEndsAtUtc, "monthly"));
        await db.SaveChangesAsync(cancellationToken);
    }
}
