using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
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

namespace ONEVO.Tests.Integration.CoreHr.PositionAssignment;

public sealed class PositionAssignmentActiveHoldersTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_position_active_holders_test")
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
            Name = "Active Holders Tenant",
            Slug = "active-holders",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task GetActiveHoldersAsync_Returns_Only_Active_PrimaryEmployment_Holders_With_Email()
    {
        var positionId = await SeedPositionAsync(maxOccupancy: 3);
        var activeHolderId = await SeedEmployeeWithActiveAssignmentAsync(positionId);
        var endedHolderId = await SeedEmployeeWithEndedAssignmentAsync(positionId);

        await using var db = CreateContext(_tenantId, "active-holders");
        var repository = PositionAssignmentRepositoryTestSupport.CreateRepository(db);

        var holders = await repository.GetActiveHoldersAsync(_tenantId, positionId, CancellationToken.None);

        holders.Should().ContainSingle(h => h.EmployeeId == activeHolderId);
        holders.Should().NotContain(h => h.EmployeeId == endedHolderId);
        holders.Single().WorkEmail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TryCreateActiveAssignmentAsync_Persists_ReportsToEmployeeId_When_Provided()
    {
        var managerId = await SeedEmployeeAsync();
        var employeeId = await SeedEmployeeAsync();
        var positionId = await SeedPositionAsync(maxOccupancy: 5);

        await using var db = CreateContext(_tenantId, "active-holders");
        var repository = PositionAssignmentRepositoryTestSupport.CreateRepository(db);

        var assignmentId = await repository.TryCreateActiveAssignmentAsync(
            _tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), _createdById,
            reportsToEmployeeId: managerId, CancellationToken.None);

        assignmentId.Should().NotBeNull();

        var stored = await db.PositionAssignments.AsNoTracking()
            .SingleAsync(pa => pa.Id == assignmentId!.Value);
        stored.ReportsToEmployeeId.Should().Be(managerId);
    }

    [Fact]
    public async Task TryCreateActiveAssignmentAsync_Triggers_Closure_Rebuild()
    {
        var managerPositionId = await SeedPositionAsync(maxOccupancy: 1);
        var managerId = await SeedEmployeeWithActiveAssignmentAsync(managerPositionId);
        var subordinatePositionId = await SeedPositionAsync(maxOccupancy: 1, reportsToPositionId: managerPositionId);
        var subordinateId = await SeedEmployeeAsync();

        await using var db = CreateContext(_tenantId, "active-holders");
        var repository = PositionAssignmentRepositoryTestSupport.CreateRepository(db);
        var closureRepository = PositionAssignmentRepositoryTestSupport.CreateClosureRepository(db);

        await repository.TryCreateActiveAssignmentAsync(
            _tenantId, subordinateId, subordinatePositionId, DateOnly.FromDateTime(DateTime.UtcNow), _createdById,
            reportsToEmployeeId: null, CancellationToken.None);

        var resolvedManagerId = await closureRepository.GetDirectManagerEmployeeIdAsync(_tenantId, subordinateId, CancellationToken.None);
        resolvedManagerId.Should().Be(managerId);
    }

    private async Task<Guid> SeedPositionAsync(int maxOccupancy, Guid? reportsToPositionId = null)
    {
        await using var db = CreateContext(_tenantId, "active-holders");
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
        await using var db = CreateContext(_tenantId, "active-holders");
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = employeeId,
            TenantId = _tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@active-holders.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        await db.SaveChangesAsync();
        return employeeId;
    }

    private async Task<Guid> SeedEmployeeWithActiveAssignmentAsync(
        Guid positionId, Guid? reportsToEmployeeId = null)
    {
        var employeeId = await SeedEmployeeAsync();
        await using var db = CreateContext(_tenantId, "active-holders");
        var repository = PositionAssignmentRepositoryTestSupport.CreateRepository(db);
        await repository.TryCreateActiveAssignmentAsync(
            _tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), _createdById,
            reportsToEmployeeId, CancellationToken.None);
        return employeeId;
    }

    private async Task<Guid> SeedEmployeeWithEndedAssignmentAsync(Guid positionId)
    {
        var employeeId = await SeedEmployeeAsync();
        await using var db = CreateContext(_tenantId, "active-holders");
        var repository = PositionAssignmentRepositoryTestSupport.CreateRepository(db);
        var assignmentId = await repository.TryCreateActiveAssignmentAsync(
            _tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), _createdById,
            reportsToEmployeeId: null, CancellationToken.None);
        await repository.EndActiveAsync(_tenantId, assignmentId!.Value, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
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
