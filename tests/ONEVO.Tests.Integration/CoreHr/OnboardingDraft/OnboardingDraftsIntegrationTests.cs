using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.OnboardingDraft;

/// <summary>
/// Exercises SaveOnboardingDraftCommandHandler end to end against real PostgreSQL (real
/// EfOnboardingDraftRepository, real EfEmployeeRepository, real xmin concurrency token,
/// real SeatEntitlementService) through the same restricted-role RLS fixture pattern as the
/// other CoreHr integration suites. Does not drive the HTTP layer (see the note on
/// EmployeesListIntegrationTests for why). Requires Docker.
/// </summary>
public sealed class OnboardingDraftsIntegrationTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "onboarding_drafts_rls_test_role";
    private const string RestrictedRolePassword = "onboarding-drafts-rls-test-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_onboarding_drafts_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityId;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Onboarding Draft RLS Tenant", Slug = "onboarding-drafts-rls", CompanySizeRange = "51-200", Status = TenantStatus.Active };
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var legalEntity = new LegalEntity { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Acme Co" };
        _legalEntityId = legalEntity.Id;
        db.LegalEntities.Add(legalEntity);

        var user = new User { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "hr@onboarding-drafts-rls.onevo.dev", PasswordHash = "not-a-real-hash", FirstName = "HR", LastName = "Starter", IsActive = true };
        _userId = user.Id;
        db.Users.Add(user);
        db.WorkModes.Add(new WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true });

        await db.SaveChangesAsync();

        await CreateRestrictedRoleAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Handle_NeverCreatesAUserRow_WhenSavingADraft()
    {
        await using var db = CreateContext(useRestrictedRole: true);
        var usersBefore = await db.Users.AsNoTracking().CountAsync();

        var handler = BuildHandler(db);
        var result = await handler.Handle(NewCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var usersAfter = await db.Users.AsNoTracking().CountAsync();
        usersAfter.Should().Be(usersBefore, "Save Draft must never create a user account");
    }

    [Fact]
    public async Task Handle_AlwaysResultsInADraftStatus_NeverFinalized()
    {
        await using var db = CreateContext(useRestrictedRole: true);
        var handler = BuildHandler(db);

        var result = await handler.Handle(NewCommand(), CancellationToken.None);

        result.Value!.Status.Should().NotBe("finalized");
        result.Value.Status.Should().Be("waiting_for_seat", "the seat service always returns Undetermined today");
    }

    [Fact]
    public async Task ResumingADraft_ReturnsTheLastSavedStep()
    {
        await using var createDb = CreateContext(useRestrictedRole: true);
        var createResult = await BuildHandler(createDb).Handle(NewCommand(lastSavedStep: "employee_details"), CancellationToken.None);
        createResult.IsSuccess.Should().BeTrue();

        await using var updateDb = CreateContext(useRestrictedRole: true);
        var updateResult = await BuildHandler(updateDb).Handle(
            NewCommand(draftId: createResult.Value!.Id, lastSavedStep: "checklist_template"), CancellationToken.None);

        updateResult.IsSuccess.Should().BeTrue();
        updateResult.Value!.LastSavedStep.Should().Be("checklist_template");
    }

    [Fact]
    public async Task ConcurrentSaveWithStaleIfMatchVersion_ReturnsConflict()
    {
        await using var createDb = CreateContext(useRestrictedRole: true);
        var created = await BuildHandler(createDb).Handle(NewCommand(), CancellationToken.None);
        var draftId = created.Value!.Id;
        var originalVersion = created.Value.Version;

        // First writer succeeds and advances the version.
        await using var firstWriterDb = CreateContext(useRestrictedRole: true);
        var firstResult = await BuildHandler(firstWriterDb).Handle(
            NewCommand(draftId: draftId, lastSavedStep: "checklist_template", ifMatch: originalVersion), CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        // Second writer still holds the now-stale original version.
        await using var secondWriterDb = CreateContext(useRestrictedRole: true);
        var secondResult = await BuildHandler(secondWriterDb).Handle(
            NewCommand(draftId: draftId, lastSavedStep: "checklist_review", ifMatch: originalVersion), CancellationToken.None);

        secondResult.IsSuccess.Should().BeFalse();
        secondResult.StatusCode.Should().Be(409);
    }

    private SaveOnboardingDraftCommandHandler BuildHandler(ApplicationDbContext db)
    {
        var draftRepository = new EfOnboardingDraftRepository(db);
        var employeeRepository = new EfEmployeeRepository(db);
        var positionRepository = new EfPositionRepository(db);
        var legalEntityRepository = new EfLegalEntityRepository(db);
        var departmentRepository = new EfDepartmentRepository(db);
        var seatEntitlementService = new SeatEntitlementService(db);
        var workModeRepository = new EfWorkModeRepository(db);
        var currentUser = new StubCurrentUser(_tenantId, _userId);

        return new SaveOnboardingDraftCommandHandler(
            draftRepository, employeeRepository, positionRepository, legalEntityRepository, departmentRepository, seatEntitlementService, workModeRepository, currentUser, _clock);
    }

    private SaveOnboardingDraftCommand NewCommand(
        Guid? draftId = null, string lastSavedStep = "employee_details", string? ifMatch = null) => new(
        draftId, "Ada", "Lovelace", $"{Guid.NewGuid():N}@onboarding-drafts-rls-test.onevo.dev", _legalEntityId, null, null,
        "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, 1, null, null, lastSavedStep, ifMatch);

    private async Task CreateRestrictedRoleAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
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
                GRANT SELECT, INSERT, UPDATE, DELETE ON onboarding_drafts TO {RestrictedRoleName};
                GRANT SELECT ON tenants, legal_entities, departments, positions, employees,
                    users, employment_types, employment_statuses, position_access_templates,
                    tenant_subscriptions, work_modes TO {RestrictedRoleName};
            ";
            await grantTables.ExecuteNonQueryAsync();
        }

        var restrictedBuilder = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString)
        {
            Username = RestrictedRoleName,
            Password = RestrictedRolePassword
        };
        _restrictedConnectionString = restrictedBuilder.ConnectionString;
    }

    private ApplicationDbContext CreateContext(bool useRestrictedRole = false)
    {
        var tenantContext = new TenantContextAccessor();
        if (useRestrictedRole)
        {
            tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(_tenantId, "onboarding-drafts-rls", TenantStatus.Active, null));
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

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid tenantId, Guid userId)
        {
            TenantId = tenantId;
            UserId = userId;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string Email => "hr@onboarding-drafts-rls.onevo.dev";
        public IReadOnlyList<string> Permissions => ["employees:write"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }
}
