using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.Features.TimeAttendance;

/// <summary>
/// Shared, one-time-per-class setup for ExpectedWorkAreaResolverIntegrationTests: clones the
/// database ONCE. xUnit's IClassFixture constructs this ONCE and disposes it once after every fact
/// in the class has run, instead of IAsyncLifetime's default of once PER fact - previously this
/// class's own InitializeAsync ran 8 times (5 facts + 3 Theory cases), once per test. Every fact
/// generates its own fresh tenant/legal-entity/employee/date scope via NewScope() (see its own
/// comment for why), so there is no cross-fact state-sharing risk from converting this class.
/// </summary>
public sealed class ExpectedWorkAreaResolverIntegrationTestsFixture : IAsyncLifetime
{
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _connectionString = configured;
            await AdminTestFactory.MigrateDatabaseAsync(_connectionString);
        }
        else
        {
            // Cloned from the shared, already-migrated template - see SharedPostgresTemplate.
            _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Every fact (and every [InlineData] case of the Theory) gets its own fresh tenant/legal-
    /// entity/employee/date tuple: this class used to share one static set of these ids across
    /// every fact, which only worked because each fact previously got its own fresh Testcontainers
    /// database (IAsyncLifetime). Under a shared IClassFixture database, a leftover approved row
    /// seeded by one fact would otherwise satisfy another fact's "no approved row exists for this
    /// exact key" assertion.
    /// </summary>
    public static (Guid TenantId, Guid LegalEntityId, Guid EmployeeId, DateOnly Date) NewScope() =>
        (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));

    public EfWorkAreaChangeRequestRepository Repository() => new(BuildDbContext());

    private ApplicationDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new InactiveTenantContext());
    }

    // These tests read directly through the repository with explicit tenant/legal-entity/employee
    // filters (the exact thing under test), so the EF global tenant query filter is deliberately
    // left inactive here - the same "System" (non-Tenant) context mode that leaves the filter
    // inactive for admin/platform contexts elsewhere in the app - rather than wiring the full
    // request-scoped tenant-resolution pipeline this direct-DbContext test does not otherwise need.
    private sealed class InactiveTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string? Slug => null;
        public TenantStatus? Status => null;
        public bool IsResolved => false;
        public TenantContextMode ContextMode => TenantContextMode.System;
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }

    public async Task SeedAsync(
        Guid tenantId, Guid legalEntityId, Guid employeeId, DateOnly date, string status, string requestedWorkArea)
    {
        // employee_id/legal_entity_id are restrictive foreign keys; these are synthetic ids that
        // don't exist in employees/legal_entities, so FK triggers are suspended for the insert
        // (matching WorkAreaChangeRequestsIntegrationTests' established technique). This does not
        // suspend the resolver's own tenant/legal-entity/employee/date/status filtering, which is
        // exactly what these tests exercise.
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using (var setReplica = connection.CreateCommand())
        {
            setReplica.CommandText = "SET session_replication_role = replica;";
            await setReplica.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO work_area_change_requests
                (id, tenant_id, employee_id, legal_entity_id, date,
                 current_expected_work_area, requested_work_area, reason, status, requested_at)
            VALUES ($1, $2, $3, $4, $5, 'onsite', $6, 'fixture', $7, now());
            """;
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(employeeId);
        command.Parameters.AddWithValue(legalEntityId);
        command.Parameters.AddWithValue(date);
        command.Parameters.AddWithValue(requestedWorkArea);
        command.Parameters.AddWithValue(status);
        await command.ExecuteNonQueryAsync();

        await using var resetReplica = connection.CreateCommand();
        resetReplica.CommandText = "RESET session_replication_role;";
        await resetReplica.ExecuteNonQueryAsync();
    }

}

/// <summary>
/// Proves that EfWorkAreaChangeRequestRepository.GetApprovedForDateAsync - the read the runtime
/// ExpectedWorkAreaResolver depends on to override the employee's permanent work mode - translates
/// correctly against real PostgreSQL, not just the EF InMemory provider used by the unit-level
/// EfWorkAreaChangeRequestRepositoryTests. Rows are seeded via a raw admin connection (matching the
/// established pattern in WorkAreaChangeRequestsIntegrationTests) and read back through the actual
/// repository class and a real Npgsql-backed ApplicationDbContext.
///
/// This intentionally does not drive the full HTTP/tenant-provisioning stack (see
/// AttendanceCorrectionsIntegrationTests for that heavier pattern) - ClockIn persistence and the
/// approval-time attendance-snapshot sync are covered at the unit level
/// (ClockInOutCommandHandlerTests, WorkAreaChangeRequestWorkflowTests) against fakes/mocks of these
/// same repository contracts; this class closes the one gap those tests cannot close: real
/// PostgreSQL LINQ translation of the new query.
/// </summary>
public sealed class ExpectedWorkAreaResolverIntegrationTests : IClassFixture<ExpectedWorkAreaResolverIntegrationTestsFixture>
{
    private readonly ExpectedWorkAreaResolverIntegrationTestsFixture _fixture;

    public ExpectedWorkAreaResolverIntegrationTests(ExpectedWorkAreaResolverIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_ReturnsApprovedRowForExactScope()
    {
        var (tenantId, legalEntityId, employeeId, date) = ExpectedWorkAreaResolverIntegrationTestsFixture.NewScope();
        await _fixture.SeedAsync(tenantId, legalEntityId, employeeId, date, WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await _fixture.Repository().GetApprovedForDateAsync(tenantId, legalEntityId, employeeId, date);

        result.Should().NotBeNull();
        result!.RequestedWorkArea.Should().Be("remote");
    }

    [Theory]
    [InlineData(WorkAreaChangeRequest.StatusPending)]
    [InlineData(WorkAreaChangeRequest.StatusRejected)]
    [InlineData(WorkAreaChangeRequest.StatusCancelled)]
    public async Task GetApprovedForDate_RealPostgres_IgnoresNonApprovedStatus(string status)
    {
        var (tenantId, legalEntityId, employeeId, date) = ExpectedWorkAreaResolverIntegrationTestsFixture.NewScope();
        await _fixture.SeedAsync(tenantId, legalEntityId, employeeId, date, status, "remote");

        var result = await _fixture.Repository().GetApprovedForDateAsync(tenantId, legalEntityId, employeeId, date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_IgnoresAnotherDate()
    {
        var (tenantId, legalEntityId, employeeId, date) = ExpectedWorkAreaResolverIntegrationTestsFixture.NewScope();
        await _fixture.SeedAsync(tenantId, legalEntityId, employeeId, date.AddDays(1), WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await _fixture.Repository().GetApprovedForDateAsync(tenantId, legalEntityId, employeeId, date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_IgnoresAnotherEmployee()
    {
        var (tenantId, legalEntityId, employeeId, date) = ExpectedWorkAreaResolverIntegrationTestsFixture.NewScope();
        var otherEmployeeId = Guid.NewGuid();
        await _fixture.SeedAsync(tenantId, legalEntityId, otherEmployeeId, date, WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await _fixture.Repository().GetApprovedForDateAsync(tenantId, legalEntityId, employeeId, date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_IgnoresAnotherLegalEntity()
    {
        var (tenantId, legalEntityId, employeeId, date) = ExpectedWorkAreaResolverIntegrationTestsFixture.NewScope();
        var otherLegalEntityId = Guid.NewGuid();
        await _fixture.SeedAsync(tenantId, otherLegalEntityId, employeeId, date, WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await _fixture.Repository().GetApprovedForDateAsync(tenantId, legalEntityId, employeeId, date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_IgnoresAnotherTenant()
    {
        var (tenantId, legalEntityId, employeeId, date) = ExpectedWorkAreaResolverIntegrationTestsFixture.NewScope();
        var otherTenantId = Guid.NewGuid();
        await _fixture.SeedAsync(otherTenantId, legalEntityId, employeeId, date, WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await _fixture.Repository().GetApprovedForDateAsync(tenantId, legalEntityId, employeeId, date);

        result.Should().BeNull();
    }

}
