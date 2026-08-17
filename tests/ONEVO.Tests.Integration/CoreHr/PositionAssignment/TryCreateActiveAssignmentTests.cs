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
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.PositionAssignment;

public sealed class TryCreateActiveAssignmentTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_try_create_active_position_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _positionId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _positionId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Try Create Active Tenant",
            Slug = "try-create-active",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });
        db.Positions.Add(new Position
        {
            Id = _positionId,
            TenantId = _tenantId,
            Name = "Unique Seat",
            PositionType = Position.TypeUnique,
            MaxOccupancy = 1,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task TryCreateActive_WhenSeatAvailable_InsertsActiveRowAndReturnsId()
    {
        var employeeId = await SeedEmployeeAsync();
        await using var db = CreateContext(_tenantId, "try-create-active");
        var repo = new EfPositionAssignmentRepository(db);

        var createdId = await repo.TryCreateActiveAssignmentAsync(
            _tenantId, employeeId, _positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        Assert.NotNull(createdId);
        var row = await db.PositionAssignments.FindAsync(createdId!.Value);
        Assert.Equal(PositionAssignmentStatus.Active, row!.AssignmentStatus);
    }

    [Fact]
    public async Task TryCreateActive_WhenPositionAtCapacity_ReturnsNull()
    {
        var employeeA = await SeedEmployeeAsync();
        var employeeB = await SeedEmployeeAsync();
        await using var db = CreateContext(_tenantId, "try-create-active");
        var repo = new EfPositionAssignmentRepository(db);

        await repo.TryCreateActiveAssignmentAsync(
            _tenantId, employeeA, _positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());
        var second = await repo.TryCreateActiveAssignmentAsync(
            _tenantId, employeeB, _positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        Assert.Null(second);
    }

    [Fact]
    public async Task EndActive_SetsEndedStatusAndEffectiveTo()
    {
        var employeeId = await SeedEmployeeAsync();
        await using var db = CreateContext(_tenantId, "try-create-active");
        var repo = new EfPositionAssignmentRepository(db);
        var createdId = await repo.TryCreateActiveAssignmentAsync(
            _tenantId, employeeId, _positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        var effectiveTo = DateOnly.FromDateTime(DateTime.UtcNow);
        var ended = await repo.EndActiveAsync(_tenantId, createdId!.Value, effectiveTo);

        Assert.True(ended);
        var row = await db.PositionAssignments.FindAsync(createdId.Value);
        Assert.Equal(PositionAssignmentStatus.Ended, row!.AssignmentStatus);
        Assert.Equal(effectiveTo, row.EffectiveTo);
    }

    private async Task<Guid> SeedEmployeeAsync()
    {
        await using var db = CreateContext(_tenantId, "try-create-active");
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = employeeId,
            TenantId = _tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@try-create-active.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        await db.SaveChangesAsync();
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
