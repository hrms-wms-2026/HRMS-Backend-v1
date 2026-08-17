using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Invite;
using ONEVO.Domain.Features.Auth.Entities;
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
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.Employee;

/// <summary>
/// Exercises ListEmployeesQueryHandler/GetEmployeeQueryHandler against real PostgreSQL with
/// RLS enforced through a restricted, non-superuser, non-BYPASSRLS role (same fixture pattern
/// as PositionAssignmentRlsIntegrationTests/RestrictedRoleRlsEnforcementTests) - the read path
/// composed end to end (handler -> repository -> real SQL), not mocked. Does NOT drive the
/// full Kestrel/WebApplicationFactory HTTP pipeline (Authorize/RequirePermissionAttribute) the
/// way DepartmentsIntegrationTests.cs does; that gap is documented in the implementation
/// report. Requires Docker.
/// </summary>
public sealed class EmployeesListIntegrationTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "employees_list_rls_test_role";
    private const string RestrictedRolePassword = "employees-list-rls-test-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_employees_list_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _legalEntityAId;
    private Guid _departmentAId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenantA = NewTenant("Employees List RLS Tenant A", "employees-list-rls-a");
        var tenantB = NewTenant("Employees List RLS Tenant B", "employees-list-rls-b");
        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;
        db.Tenants.AddRange(tenantA, tenantB);

        var legalEntityA = new LegalEntity { Id = Guid.NewGuid(), TenantId = _tenantAId, Name = "Acme Co" };
        _legalEntityAId = legalEntityA.Id;
        db.LegalEntities.Add(legalEntityA);

        var departmentA = new Department { Id = Guid.NewGuid(), TenantId = _tenantAId, LegalEntityId = _legalEntityAId, Name = "Engineering" };
        _departmentAId = departmentA.Id;
        db.Departments.Add(departmentA);

        for (var i = 1; i <= 30; i++)
        {
            var employee = NewEmployee(_tenantAId, $"E-{i:000}");
            employee.LegalEntityId = _legalEntityAId;
            employee.DepartmentId = _departmentAId;
            db.Employees.Add(employee);
        }

        db.Employees.Add(NewEmployee(_tenantBId, "E-B-001"));

        await db.SaveChangesAsync();

        await CreateRestrictedRoleAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task List_OnlyReturnsEmployeesBelongingToCallersTenant()
    {
        var handler = BuildListHandler(_tenantAId, orgManage: true);
        var resultA = await handler.Handle(new ListEmployeesQuery(null, null, null, 1, 100), CancellationToken.None);

        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.TotalCount.Should().Be(30);
        resultA.Value.Items.Should().OnlyContain(i => i.LegalEntityId == _legalEntityAId || i.LegalEntityId == null);

        var handlerB = BuildListHandler(_tenantBId, orgManage: true);
        var resultB = await handlerB.Handle(new ListEmployeesQuery(null, null, null, 1, 100), CancellationToken.None);

        resultB.Value!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task List_RespectsPageSize_AndReturnsStableOrderAcrossPages()
    {
        var handler = BuildListHandler(_tenantAId, orgManage: true);

        var page1 = await handler.Handle(new ListEmployeesQuery(null, null, null, 1, 10), CancellationToken.None);
        var page2 = await handler.Handle(new ListEmployeesQuery(null, null, null, 2, 10), CancellationToken.None);

        page1.Value!.Items.Should().HaveCount(10);
        page2.Value!.Items.Should().HaveCount(10);
        var page1Ids = page1.Value.Items.Select(i => i.Id).ToHashSet();
        var page2Ids = page2.Value.Items.Select(i => i.Id).ToHashSet();
        page1Ids.Intersect(page2Ids).Should().BeEmpty("pages must not overlap");
    }

    [Fact]
    public async Task List_SearchFiltersByEmployeeNumber()
    {
        var handler = BuildListHandler(_tenantAId, orgManage: true);

        var result = await handler.Handle(new ListEmployeesQuery("E-015", null, null, 1, 25), CancellationToken.None);

        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Single().EmployeeNumber.Should().Be("E-015");
    }

    [Fact]
    public async Task List_FiltersByDepartmentId()
    {
        var handler = BuildListHandler(_tenantAId, orgManage: true);

        var result = await handler.Handle(new ListEmployeesQuery(null, _departmentAId, null, 1, 100), CancellationToken.None);

        result.Value!.TotalCount.Should().Be(30);
        result.Value.Items.Should().OnlyContain(i => i.DepartmentId == _departmentAId);
    }

    [Fact]
    public async Task List_WithoutOrgManage_ReturnsOnlySelf_WhenCallerHasNoResolvableCoverage()
    {
        Guid selfEmployeeId;
        Guid selfUserId;
        await using (var seedDb = CreateContext(_tenantAId, "employees-list-rls-a", useRestrictedRole: true))
        {
            var self = await seedDb.Employees.AsNoTracking().Select(e => new { e.Id, e.UserId }).FirstAsync();
            selfEmployeeId = self.Id;
            selfUserId = self.UserId;
        }

        // callerOwnEmployeeId here is threaded through as the session's UserId (matching
        // employees.user_id, resolved by EmployeeVisibilityScopeResolver) - not the employee's
        // own row id, which is what the resolver looks up FROM the user id.
        var handler = BuildListHandler(_tenantAId, orgManage: false, callerOwnEmployeeId: selfUserId);
        var result = await handler.Handle(new ListEmployeesQuery(null, null, null, 1, 100), CancellationToken.None);

        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Single().Id.Should().Be(selfEmployeeId);
    }

    [Fact]
    public async Task GetById_Returns404_ForEmployeeInAnotherTenant()
    {
        Guid tenantBEmployeeId;
        await using (var seedDb = CreateContext(_tenantBId, "employees-list-rls-b", useRestrictedRole: true))
        {
            tenantBEmployeeId = await seedDb.Employees.AsNoTracking().Select(e => e.Id).FirstAsync();
        }

        var handler = BuildGetHandler(_tenantAId, orgManage: true);
        var result = await handler.Handle(new GetEmployeeQuery(tenantBEmployeeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_Returns200_ForVisibleEmployeeInCallersTenant()
    {
        Guid employeeId;
        await using (var seedDb = CreateContext(_tenantAId, "employees-list-rls-a", useRestrictedRole: true))
        {
            employeeId = await seedDb.Employees.AsNoTracking().Select(e => e.Id).FirstAsync();
        }

        var handler = BuildGetHandler(_tenantAId, orgManage: true);
        var result = await handler.Handle(new GetEmployeeQuery(employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(employeeId);
    }

    private ListEmployeesQueryHandler BuildListHandler(Guid tenantId, bool orgManage, Guid? callerOwnEmployeeId = null)
    {
        var db = CreateContext(tenantId, SlugFor(tenantId), useRestrictedRole: true);
        var employeeRepository = new EfEmployeeRepository(db);
        var scopeResolver = new EmployeeVisibilityScopeResolver(db);
        var currentUser = BuildCurrentUser(tenantId, orgManage, callerOwnEmployeeId);

        return new ListEmployeesQueryHandler(employeeRepository, scopeResolver, currentUser);
    }

    private GetEmployeeQueryHandler BuildGetHandler(Guid tenantId, bool orgManage, Guid? callerOwnEmployeeId = null)
    {
        var db = CreateContext(tenantId, SlugFor(tenantId), useRestrictedRole: true);
        var employeeRepository = new EfEmployeeRepository(db);
        var scopeResolver = new EmployeeVisibilityScopeResolver(db);
        var currentUser = BuildCurrentUser(tenantId, orgManage, callerOwnEmployeeId);

        return new GetEmployeeQueryHandler(
            employeeRepository,
            scopeResolver,
            new EfInvitationTokenRepository(db),
            currentUser,
            _clock);
    }

    private static ICurrentUser BuildCurrentUser(Guid tenantId, bool orgManage, Guid? callerOwnEmployeeId)
        => new StubCurrentUser(tenantId, callerOwnEmployeeId ?? Guid.NewGuid(), orgManage);

    private sealed class StubCurrentUser : ICurrentUser
    {
        private readonly bool _hasOrgManage;

        public StubCurrentUser(Guid tenantId, Guid userId, bool hasOrgManage)
        {
            TenantId = tenantId;
            UserId = userId;
            _hasOrgManage = hasOrgManage;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string Email => "test@employees-list-rls-test.onevo.dev";
        public IReadOnlyList<string> Permissions => _hasOrgManage ? ["org:manage", "employees:read"] : ["employees:read"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }

    private string SlugFor(Guid tenantId) => tenantId == _tenantAId ? "employees-list-rls-a" : "employees-list-rls-b";

    private async Task CreateRestrictedRoleAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using (var createRole = connection.CreateCommand())
        {
            createRole.CommandText = $@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{RestrictedRoleName}') THEN
                        CREATE ROLE {RestrictedRoleName}
                            LOGIN PASSWORD '{RestrictedRolePassword}' NOSUPERUSER NOBYPASSRLS;
                    END IF;
                END
                $$;
            ";
            await createRole.ExecuteNonQueryAsync();
        }

        await using (var grantSchema = connection.CreateCommand())
        {
            grantSchema.CommandText = $"GRANT USAGE ON SCHEMA public TO {RestrictedRoleName};";
            await grantSchema.ExecuteNonQueryAsync();
        }

        await using (var grantTables = connection.CreateCommand())
        {
            grantTables.CommandText = $@"
                GRANT SELECT ON employees, position_assignments, employee_hierarchy_closure,
                    departments, legal_entities, positions, employment_types, employment_statuses,
                    management_coverage_records, tenants, invitation_tokens
                    TO {RestrictedRoleName};
            ";
            await grantTables.ExecuteNonQueryAsync();
        }

        var restrictedBuilder = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Username = RestrictedRoleName,
            Password = RestrictedRolePassword
        };
        _restrictedConnectionString = restrictedBuilder.ConnectionString;
    }

    private static Tenant NewTenant(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        CompanySizeRange = "51-200",
        Status = TenantStatus.Active
    };

    private static EmployeeEntity NewEmployee(Guid tenantId, string employeeNumber) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = employeeNumber,
        FirstName = "Test",
        LastName = employeeNumber,
        Email = $"{Guid.NewGuid():N}@employees-list-rls-test.onevo.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null, bool useRestrictedRole = false)
    {
        var tenantContext = new TenantContextAccessor();

        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(tenantId.Value, slug, TenantStatus.Active, null));
        }

        var connectionString = useRestrictedRole ? _restrictedConnectionString : _connectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
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
