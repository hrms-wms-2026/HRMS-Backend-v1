using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.EmployeeHierarchyClosure;

public sealed class EmployeeHierarchyClosureRebuildTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_hierarchy_closure_rebuild_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private readonly Guid _createdById = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Hierarchy Closure Tenant",
            Slug = "hierarchy-closure",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task RebuildAsync_Resolves_Unique_Position_Target_Automatically()
    {
        var managerPositionId = await SeedPositionAsync(maxOccupancy: 1);
        var managerId = await SeedEmployeeWithActiveAssignmentAsync(managerPositionId);
        var subordinatePositionId = await SeedPositionAsync(maxOccupancy: 1, reportsToPositionId: managerPositionId);
        var subordinateId = await SeedEmployeeWithActiveAssignmentAsync(subordinatePositionId);

        await using var db = CreateContext(_tenantId, "hierarchy-closure");
        var closureRepository = PositionAssignmentRepositoryTestSupport.CreateClosureRepository(db);
        await closureRepository.RebuildAsync(_tenantId, CancellationToken.None);

        var resolvedManagerId = await closureRepository.GetDirectManagerEmployeeIdAsync(_tenantId, subordinateId, CancellationToken.None);
        resolvedManagerId.Should().Be(managerId);
    }

    [Fact]
    public async Task RebuildAsync_Leaves_No_Row_When_Pooled_Target_Has_No_Override()
    {
        var pooledPositionId = await SeedPositionAsync(maxOccupancy: 2);
        await SeedEmployeeWithActiveAssignmentAsync(pooledPositionId);
        await SeedEmployeeWithActiveAssignmentAsync(pooledPositionId);
        var subordinatePositionId = await SeedPositionAsync(maxOccupancy: 1, reportsToPositionId: pooledPositionId);
        var subordinateId = await SeedEmployeeWithActiveAssignmentAsync(subordinatePositionId, reportsToEmployeeId: null);

        await using var db = CreateContext(_tenantId, "hierarchy-closure");
        var closureRepository = PositionAssignmentRepositoryTestSupport.CreateClosureRepository(db);
        await closureRepository.RebuildAsync(_tenantId, CancellationToken.None);

        var resolvedManagerId = await closureRepository.GetDirectManagerEmployeeIdAsync(_tenantId, subordinateId, CancellationToken.None);
        resolvedManagerId.Should().BeNull();
    }

    [Fact]
    public async Task RebuildAsync_Resolves_Pooled_Target_Via_ReportsToEmployeeId_Override()
    {
        var pooledPositionId = await SeedPositionAsync(maxOccupancy: 2);
        var chosenHolderId = await SeedEmployeeWithActiveAssignmentAsync(pooledPositionId);
        await SeedEmployeeWithActiveAssignmentAsync(pooledPositionId);
        var subordinatePositionId = await SeedPositionAsync(maxOccupancy: 1, reportsToPositionId: pooledPositionId);
        var subordinateId = await SeedEmployeeWithActiveAssignmentAsync(subordinatePositionId, reportsToEmployeeId: chosenHolderId);

        await using var db = CreateContext(_tenantId, "hierarchy-closure");
        var closureRepository = PositionAssignmentRepositoryTestSupport.CreateClosureRepository(db);
        await closureRepository.RebuildAsync(_tenantId, CancellationToken.None);

        var resolvedManagerId = await closureRepository.GetDirectManagerEmployeeIdAsync(_tenantId, subordinateId, CancellationToken.None);
        resolvedManagerId.Should().Be(chosenHolderId);
    }

    private async Task<Guid> SeedPositionAsync(int maxOccupancy, Guid? reportsToPositionId = null)
    {
        await using var db = CreateContext(_tenantId, "hierarchy-closure");
        var positionId = Guid.NewGuid();
        db.Positions.Add(new Position
        {
            Id = positionId,
            TenantId = _tenantId,
            Name = $"Position {positionId:N}"[..20],
            PositionType = maxOccupancy == 1 ? Position.TypeUnique : Position.TypePooled,
            MaxOccupancy = maxOccupancy,
            ReportsToPositionId = reportsToPositionId,
        });
        await db.SaveChangesAsync();
        return positionId;
    }

    private async Task<Guid> SeedEmployeeAsync()
    {
        await using var db = CreateContext(_tenantId, "hierarchy-closure");
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = employeeId,
            TenantId = _tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@hierarchy-closure.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        await db.SaveChangesAsync();
        return employeeId;
    }

    private async Task<Guid> SeedEmployeeWithActiveAssignmentAsync(
        Guid positionId, Guid? reportsToEmployeeId = null)
    {
        var employeeId = await SeedEmployeeAsync();
        await using var db = CreateContext(_tenantId, "hierarchy-closure");
        var repository = PositionAssignmentRepositoryTestSupport.CreateRepository(db);
        await repository.TryCreateActiveAssignmentAsync(
            _tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), _createdById,
            reportsToEmployeeId, CancellationToken.None);
        return employeeId;
    }

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null)
    {
        var tenantContext = new TenantContextAccessor();
        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(
                tenantId.Value, slug, TenantStatus.Active, null));
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
