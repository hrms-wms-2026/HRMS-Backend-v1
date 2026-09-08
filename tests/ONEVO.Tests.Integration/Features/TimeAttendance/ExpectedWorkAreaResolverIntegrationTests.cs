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
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Features.TimeAttendance;

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
public sealed class ExpectedWorkAreaResolverIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _postgres;
    private string _connectionString = null!;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid OtherLegalEntityId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.UtcNow);

    public ExpectedWorkAreaResolverIntegrationTests()
    {
        var configured = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (!string.IsNullOrWhiteSpace(configured))
            return;

        _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("onevo_work_area_resolver_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
        }
        else
        {
            _connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB")!;
        }

        await AdminTestFactory.MigrateDatabaseAsync(_connectionString);
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_ReturnsApprovedRowForExactScope()
    {
        await SeedAsync(TenantId, LegalEntityId, EmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await Repository().GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().NotBeNull();
        result!.RequestedWorkArea.Should().Be("remote");
    }

    [Theory]
    [InlineData(WorkAreaChangeRequest.StatusPending)]
    [InlineData(WorkAreaChangeRequest.StatusRejected)]
    [InlineData(WorkAreaChangeRequest.StatusCancelled)]
    public async Task GetApprovedForDate_RealPostgres_IgnoresNonApprovedStatus(string status)
    {
        await SeedAsync(TenantId, LegalEntityId, EmployeeId, Date, status, "remote");

        var result = await Repository().GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_IgnoresAnotherDate()
    {
        await SeedAsync(TenantId, LegalEntityId, EmployeeId, Date.AddDays(1), WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await Repository().GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_IgnoresAnotherEmployee()
    {
        await SeedAsync(TenantId, LegalEntityId, OtherEmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await Repository().GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_IgnoresAnotherLegalEntity()
    {
        await SeedAsync(TenantId, OtherLegalEntityId, EmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await Repository().GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_RealPostgres_IgnoresAnotherTenant()
    {
        await SeedAsync(OtherTenantId, LegalEntityId, EmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote");

        var result = await Repository().GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    private EfWorkAreaChangeRequestRepository Repository() => new(BuildDbContext());

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

    private async Task SeedAsync(
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
