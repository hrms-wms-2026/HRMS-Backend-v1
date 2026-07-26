using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

/// <summary>
/// Proves ApplicationDbContext's tenant HasQueryFilter is safe under EF Core's
/// process-wide compiled-model cache. All three contexts below share the
/// exact same DbContextOptions instance, which is how EF Core keys its
/// internal model cache — this is the same sharing that happens in
/// production, where every request builds a DbContextOptions with identical
/// configuration via AddDbContext. Before the fix, the tenant filter closed
/// over the specific ITenantContext object passed to whichever
/// ApplicationDbContext instance triggered the first model build, so every
/// later instance silently reused that first instance's tenant/mode — this
/// test fails against that old pattern because dbA and dbB would both see
/// both tenants' rows (the seed context's ContextMode is System, which
/// bypasses the filter entirely).
/// </summary>
public class ApplicationDbContextTenantFilterTests
{
    private static readonly DbContextOptions<ApplicationDbContext> SharedOptions =
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("tenant_filter_model_cache_test")
            .UseSnakeCaseNamingConvention()
            .Options;

    [Fact]
    public async Task TwoDbContextInstances_WithDifferentTenants_OnlySeeTheirOwnTenantRows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seeded through a System-mode context first, exactly mirroring how
        // ApplicationDbContextFactory (migrations) constructs its own
        // unresolved TenantContextAccessor — this is the instance whose
        // ITenantContext the old buggy pattern would have frozen into the
        // shared compiled model.
        await using (var seedDb = CreateContext(new TenantContextAccessor()))
        {
            seedDb.Roles.Add(new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Tenant A Role" });
            seedDb.Roles.Add(new Role { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Tenant B Role" });
            await seedDb.SaveChangesAsync();
        }

        var tenantAContext = new TenantContextAccessor();
        tenantAContext.Resolve(new TenantRegistryEntry(tenantA, "tenant-a", TenantStatus.Active, null));
        await using var dbA = CreateContext(tenantAContext);
        var rolesVisibleToTenantA = await dbA.Roles.ToListAsync();

        var tenantBContext = new TenantContextAccessor();
        tenantBContext.Resolve(new TenantRegistryEntry(tenantB, "tenant-b", TenantStatus.Active, null));
        await using var dbB = CreateContext(tenantBContext);
        var rolesVisibleToTenantB = await dbB.Roles.ToListAsync();

        // Asserted as an exact count + OnlyContain, not ContainSingle(predicate) —
        // ContainSingle(predicate) only checks that one matching item exists, so
        // it would still pass even if the other tenant's row leaked through
        // alongside it.
        rolesVisibleToTenantA.Should().HaveCount(1);
        rolesVisibleToTenantA.Should().OnlyContain(r => r.TenantId == tenantA);

        rolesVisibleToTenantB.Should().HaveCount(1);
        rolesVisibleToTenantB.Should().OnlyContain(r => r.TenantId == tenantB);
    }

    private static ApplicationDbContext CreateContext(ITenantContext tenantContext) =>
        new ApplicationDbContext(
            SharedOptions,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
}
