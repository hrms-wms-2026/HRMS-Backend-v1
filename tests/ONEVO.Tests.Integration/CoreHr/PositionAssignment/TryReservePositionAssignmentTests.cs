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
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.PositionAssignment;

public sealed class TryReservePositionAssignmentTests : IAsyncLifetime
{

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _positionId;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();

        await using var db = CreateContext();

        _tenantId = Guid.NewGuid();
        _positionId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Try Reserve Tenant",
            Slug = "try-reserve",
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

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TryReserve_WhenSeatAvailable_InsertsPlannedRowAndReturnsId()
    {
        var employeeId = await SeedEmployeeAsync();
        await using var db = CreateContext(_tenantId, "try-reserve");
        var repo = PositionAssignmentRepositoryTestSupport.CreateRepository(db);

        var reservedId = await repo.TryReservePositionAssignmentAsync(
            _tenantId, employeeId, _positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty, reportsToEmployeeId: null);

        Assert.NotNull(reservedId);
        var row = await db.PositionAssignments.FindAsync(reservedId!.Value);
        Assert.NotNull(row);
        Assert.Equal(PositionAssignmentStatus.Planned, row!.AssignmentStatus);
    }

    [Fact]
    public async Task TryReserve_WhenPositionAlreadyAtCapacityFromAnotherPlannedRow_ReturnsNullAndInsertsNothing()
    {
        var employeeA = await SeedEmployeeAsync();
        var employeeB = await SeedEmployeeAsync();
        await using var db = CreateContext(_tenantId, "try-reserve");
        var repo = PositionAssignmentRepositoryTestSupport.CreateRepository(db);

        var first = await repo.TryReservePositionAssignmentAsync(
            _tenantId, employeeA, _positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty, reportsToEmployeeId: null);
        Assert.NotNull(first);

        var second = await repo.TryReservePositionAssignmentAsync(
            _tenantId, employeeB, _positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty, reportsToEmployeeId: null);

        Assert.Null(second);
        var count = await db.PositionAssignments.CountAsync(pa => pa.PositionId == _positionId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task TryReserve_TwoConcurrentCallsAgainstOneVacancy_ExactlyOneSucceeds()
    {
        var employeeA = await SeedEmployeeAsync();
        var employeeB = await SeedEmployeeAsync();
        var repoA = PositionAssignmentRepositoryTestSupport.CreateRepository(CreateContext(_tenantId, "try-reserve"));
        var repoB = PositionAssignmentRepositoryTestSupport.CreateRepository(CreateContext(_tenantId, "try-reserve"));

        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var taskA = repoA.TryReservePositionAssignmentAsync(_tenantId, employeeA, _positionId, effectiveFrom, Guid.Empty, reportsToEmployeeId: null);
        var taskB = repoB.TryReservePositionAssignmentAsync(_tenantId, employeeB, _positionId, effectiveFrom, Guid.Empty, reportsToEmployeeId: null);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r is not null);
        Assert.Single(results, r => r is null);
    }

    [Fact]
    public async Task TryReserve_ManyConcurrentCallsAgainstOneVacancy_ExactlyOneSucceeds()
    {
        // The capacity check and the insert are one statement; without locks on the counted range,
        // two callers on SQL Server can both read a count of 0 and both insert. Eight callers make
        // that race likely enough to fail reliably if the locking is removed.
        const int callers = 8;
        var employees = new List<Guid>();
        for (var i = 0; i < callers; i++)
            employees.Add(await SeedEmployeeAsync());
        // created_by_id has a key to users, so the creator must be a real user.
        var createdById = await TestUsers.NewAsync(() => CreateContext(_tenantId, "try-reserve"), _tenantId);
        var contexts = employees.Select(_ => CreateContext(_tenantId, "try-reserve")).ToList();
        try
        {
            var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
            using var start = new SemaphoreSlim(0, callers);
            var calls = employees.Select((employeeId, i) => Task.Run(async () =>
            {
                var repo = PositionAssignmentRepositoryTestSupport.CreateRepository(contexts[i]);
                await contexts[i].Database.OpenConnectionAsync();
                await start.WaitAsync();
                return await repo.TryReservePositionAssignmentAsync(
                    _tenantId, employeeId, _positionId, effectiveFrom, createdById, reportsToEmployeeId: null);
            })).ToList();
            start.Release(callers);
            var results = await Task.WhenAll(calls);

            Assert.Single(results, r => r is not null);
            await using var db = CreateContext(_tenantId, "try-reserve");
            Assert.Equal(1, await db.PositionAssignments.CountAsync(pa => pa.PositionId == _positionId));
        }
        finally
        {
            foreach (var context in contexts)
                await context.DisposeAsync();
        }
    }

    private async Task<Guid> SeedEmployeeAsync()
    {
        await using var db = CreateContext(_tenantId, "try-reserve");
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = employeeId,
            TenantId = _tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@try-reserve.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        TestUsers.AddForNewEmployees(db);
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
            .UseTestSqlServer(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddTestRls(new TenantRlsInterceptor(tenantContext))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
