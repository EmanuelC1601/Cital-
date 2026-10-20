using Belleza.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Belleza.Infrastructure.Persistence;

/// <summary>Contexto SQL Server de Identity y del dominio de negocio.</summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Business> Businesses => Set<Business>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Business>(entity =>
        {
            entity.HasKey(business => business.Id);
            entity.Property(business => business.Name).HasMaxLength(100).IsRequired();
            entity.Property(business => business.Slug).HasMaxLength(64).IsRequired();
            entity.HasIndex(business => business.Slug).IsUnique();
            entity.Property(business => business.OwnerUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(business => business.OwnerUserId).IsUnique();
            entity.Property(business => business.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(business => business.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
