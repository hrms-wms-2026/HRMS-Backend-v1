using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Auth;

public sealed class ListRolePermissionCodesWithModulesEntityFilterTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_role_perm_entity_filter_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _userId;
    private Guid _legalEntityAId;
    private Guid _legalEntityBId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _userId = Guid.NewGuid();
        _legalEntityAId = Guid.NewGuid();
        _legalEntityBId = Guid.NewGuid();
        var positionAId = Guid.NewGuid();
        var positionBId = Guid.NewGuid();
        var permAId = Guid.NewGuid();
        var permBId = Guid.NewGuid();
        var roleAId = Guid.NewGuid();
        var roleBId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Entity Filter Tenant",
            Slug = "entity-filter",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });
        db.LegalEntities.AddRange(
            new LegalEntity { Id = _legalEntityAId, TenantId = _tenantId, Name = "Company A" },
            new LegalEntity { Id = _legalEntityBId, TenantId = _tenantId, Name = "Company B" });
        db.Users.Add(new User
        {
            Id = _userId,
            TenantId = _tenantId,
            Email = "multi@example.com",
            FirstName = "Multi",
            LastName = "Entity",
            IsActive = true,
        });
        db.Positions.AddRange(
            new Position { Id = positionAId, TenantId = _tenantId, LegalEntityId = _legalEntityAId, Name = "Role A", CreatedById = _userId },
            new Position { Id = positionBId, TenantId = _tenantId, LegalEntityId = _legalEntityBId, Name = "Role B", CreatedById = _userId });
        db.Permissions.AddRange(
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = permAId, Code = "p3-entity-a:read", Module = "core_hr", Description = "A" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = permBId, Code = "p3-entity-b:read", Module = "core_hr", Description = "B" });
        db.Roles.AddRange(
            new Role { Id = roleAId, TenantId = _tenantId, Name = "Entity A Role", CreatedById = _userId },
            new Role { Id = roleBId, TenantId = _tenantId, Name = "Entity B Role", CreatedById = _userId });
        db.RolePermissions.AddRange(
            new RolePermission { TenantId = _tenantId, RoleId = roleAId, PermissionId = permAId },
            new RolePermission { TenantId = _tenantId, RoleId = roleBId, PermissionId = permBId });
        db.UserRoles.AddRange(
            new UserRole
            {
                TenantId = _tenantId,
                UserId = _userId,
                RoleId = roleAId,
                AssignedBy = _userId,
                SourcePositionId = positionAId
            },
            new UserRole
            {
                TenantId = _tenantId,
                UserId = _userId,
                RoleId = roleBId,
                AssignedBy = _userId,
                SourcePositionId = positionBId
            });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task ListRolePermissionCodesWithModulesAsync_FiltersByActiveLegalEntity()
    {
        await using var db = CreateContext(_tenantId, "entity-filter");
        var repo = new EfAuthRepository(db);
        var now = DateTimeOffset.UtcNow;

        var entityA = await repo.ListRolePermissionCodesWithModulesAsync(_userId, now, _legalEntityAId);
        var entityB = await repo.ListRolePermissionCodesWithModulesAsync(_userId, now, _legalEntityBId);
        var unfiltered = await repo.ListRolePermissionCodesWithModulesAsync(_userId, now, null);

        entityA.Select(p => p.Code).Should().Equal("p3-entity-a:read");
        entityB.Select(p => p.Code).Should().Equal("p3-entity-b:read");
        unfiltered.Select(p => p.Code).Should().BeEquivalentTo("p3-entity-a:read", "p3-entity-b:read");
    }

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null)
    {
        var tenantContext = new TenantContextAccessor();
        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new TenantRegistryEntry(tenantId.Value, slug, TenantStatus.Active, null));
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
