using Citalu.Domain;

namespace Citalu.Application.Businesses;

/// <summary>Persistencia mínima necesaria para crear y consultar el negocio propio.</summary>
public interface IBusinessStore
{
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);
    Task<Business?> FindForOwnerAsync(string ownerUserId, CancellationToken cancellationToken);
    Task AddAsync(Business business, CancellationToken cancellationToken);
}
