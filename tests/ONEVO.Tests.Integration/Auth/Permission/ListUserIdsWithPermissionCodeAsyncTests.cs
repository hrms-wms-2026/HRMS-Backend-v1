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
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Auth.Permission;

/// <summary>
/// Integration coverage for IPermissionRepository.ListUserIdsWithPermissionCodeAsync.
/// No shared SeedTenantAsync helpers exist in this suite — seeding follows the same
/// Testcontainers + MigrateAsync pattern as ListRolePermissionCodesWithModulesEntityFilterTests.
/// </summary>
public sealed class ListUserIdsWithPermissionCodeAsyncTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_list_user_ids_with_perm_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task ReturnsEveryUserHoldingThePermission_WithinTheTenant()
    {
        var tenantId = await SeedTenantAsync("list-perm-tenant-a");
        var otherTenantId = await SeedTenantAsync("list-perm-tenant-b");
        var roleId = await SeedRoleWithPermissionAsync(tenantId, "roles:manage");
        var userA = await SeedUserWithRoleAsync(tenantId, roleId);
        var userB = await SeedUserWithRoleAsync(tenantId, roleId);
        var userWithoutRole = await SeedUserAsync(tenantId);
        var otherTenantRoleId = await SeedRoleWithPermissionAsync(otherTenantId, "roles:manage");
        await SeedUserWithRoleAsync(otherTenantId, otherTenantRoleId);

        await using var db = CreateContext(tenantId, "list-perm-tenant-a");
        var repo = new EfAuthRepository(db);
        var result = await repo.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", DateTimeOffset.UtcNow);

        result.Should().Contain(userA);
        result.Should().Contain(userB);
        result.Should().NotContain(userWithoutRole);
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExcludesExpiredUserRoles()
    {
        var tenantId = await SeedTenantAsync("list-perm-expired");
        var roleId = await SeedRoleWithPermissionAsync(tenantId, "roles:manage");
        var expiredUser = await SeedUserWithRoleAsync(tenantId, roleId, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        await using var db = CreateContext(tenantId, "list-perm-expired");
        var repo = new EfAuthRepository(db);
        var result = await repo.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", DateTimeOffset.UtcNow);

        result.Should().NotContain(expiredUser);
    }

    private async Task<Guid> SeedTenantAsync(string slug)
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"Tenant {slug}",
            Slug = slug,
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> SeedRoleWithPermissionAsync(Guid tenantId, string permissionCode)
    {
        await using var db = CreateContext();
        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == permissionCode);
        if (permission is null)
        {
            permission = new ONEVO.Domain.Features.Auth.Entities.Permission
            {
                Id = Guid.NewGuid(),
                Code = permissionCode,
                Module = "roles",
                Description = permissionCode,
            };
            db.Permissions.Add(permission);
        }

        var roleId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = $"Role-{roleId:N}"[..20],
            CreatedById = creatorId,
        });
        db.RolePermissions.Add(new RolePermission
        {
            TenantId = tenantId,
            RoleId = roleId,
            PermissionId = permission.Id,
        });
        await db.SaveChangesAsync();
        return roleId;
    }

    private async Task<Guid> SeedUserAsync(Guid tenantId)
    {
        var userId = Guid.NewGuid();
        await using var db = CreateContext();
        db.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Email = $"{userId:N}@example.com",
            FirstName = "User",
            LastName = "Seed",
            IsActive = true,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<Guid> SeedUserWithRoleAsync(
        Guid tenantId,
        Guid roleId,
        DateTimeOffset? expiresAt = null)
    {
        var userId = await SeedUserAsync(tenantId);
        await using var db = CreateContext();
        db.UserRoles.Add(new UserRole
        {
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedBy = userId,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
        return userId;
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
