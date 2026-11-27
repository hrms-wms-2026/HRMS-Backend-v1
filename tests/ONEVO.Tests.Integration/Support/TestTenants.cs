using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// The tenants row behind a fixture tenant id. Since task 39d, every tenant_id column has a foreign key to tenants,
/// so a fixture that writes a made-up tenant id must add the tenant too.
/// </summary>
public static class TestTenants
{
    public static Tenant For(Guid tenantId) => new()
    {
        Id = tenantId,
        Name = "Fixture Tenant",
        Slug = $"fx-{tenantId:N}"[..20],
        CompanySizeRange = "51-200",
        Status = TenantStatus.Active,
    };

    /// <summary>Adds the tenant unless the context already tracks it. Call SaveChanges as usual.</summary>
    public static Guid Add(ApplicationDbContext db, Guid tenantId)
    {
        if (!db.Tenants.Local.Any(t => t.Id == tenantId))
            db.Tenants.Add(For(tenantId));
        return tenantId;
    }

    /// <summary>A new tenant id whose tenants row is already saved, through a context of its own.</summary>
    public static async Task<Guid> NewAsync(Func<ApplicationDbContext> createContext)
    {
        var tenantId = Guid.NewGuid();
        await using var db = createContext();
        Add(db, tenantId);
        await db.SaveChangesAsync();
        return tenantId;
    }
}
