using Citalu.Application.Businesses;
using Citalu.Domain;

namespace Citalu.Application.Tests;

public sealed class CreateBusinessTests
{
    [Fact]
    public async Task HandleAsync_CreatesNormalizedSlugAndThirtyDayTrial()
    {
        var store = new InMemoryBusinessStore();
        var handler = new CreateBusinessHandler(store, new FrozenTimeProvider(DateTimeOffset.Parse("2026-10-20T12:00:00Z")));

        var result = await handler.HandleAsync(new CreateBusinessCommand("owner-1", "Estética Árbol Verde"), CancellationToken.None);

        Assert.Equal("estetica-arbol-verde", result.Slug);
        Assert.Equal(DateTimeOffset.Parse("2026-11-19T12:00:00Z"), result.TrialEndsAtUtc);
    }

    [Fact]
    public async Task HandleAsync_AddsSuffixWhenSlugIsAlreadyTaken()
    {
        var store = new InMemoryBusinessStore();
        store.Slugs.Add("casa-luna");
        var handler = new CreateBusinessHandler(store, TimeProvider.System);

        var result = await handler.HandleAsync(new CreateBusinessCommand("owner-1", "Casa Luna"), CancellationToken.None);

        Assert.Equal("casa-luna-2", result.Slug);
    }

    [Fact]
    public async Task HandleAsync_DoesNotAllowMoreThanOneBusinessForOwner()
    {
        var store = new InMemoryBusinessStore();
        var handler = new CreateBusinessHandler(store, TimeProvider.System);
        await handler.HandleAsync(new CreateBusinessCommand("owner-1", "Casa Luna"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new CreateBusinessCommand("owner-1", "Casa Sol"), CancellationToken.None));
    }

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Este sustituto prueba reglas de aplicación; la persistencia y la concurrencia necesitan SQL Server real.
    private sealed class InMemoryBusinessStore : IBusinessStore
    {
        private readonly List<Business> _businesses = [];
        public HashSet<string> Slugs { get; } = [];

        public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Slugs.Contains(slug) || _businesses.Any(item => item.Slug == slug));

        public Task<Business?> FindForOwnerAsync(string ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(_businesses.SingleOrDefault(item => item.OwnerUserId == ownerUserId));

        public Task AddAsync(Business business, CancellationToken cancellationToken)
        {
            _businesses.Add(business);
            return Task.CompletedTask;
        }
    }
}
