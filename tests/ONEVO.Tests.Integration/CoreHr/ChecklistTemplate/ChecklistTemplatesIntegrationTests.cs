using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
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
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.ChecklistTemplate;

/// <summary>
/// Exercises EfChecklistTemplateRepository and EfEmployeeChecklistTaskRepository end to end
/// against real PostgreSQL (real RLS, real FK constraints), through the same restricted-role
/// RLS fixture pattern as OnboardingDraftsIntegrationTests. Does not drive the HTTP layer or
/// the full Finalize/Approve handlers (those already have focused unit coverage against their
/// own mocked repositories - see FinalizeOnboardingDraftCommandHandlerTests). Requires Docker.
/// </summary>
public sealed class ChecklistTemplatesIntegrationTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "checklist_templates_rls_test_role";
    private const string RestrictedRolePassword = "checklist-templates-rls-test-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_checklist_templates_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantId;
    private Guid _otherTenantId;
    private Guid _legalEntityId;
    private Guid _departmentId;
    private Guid _positionId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Checklist Template RLS Tenant", Slug = "checklist-templates-rls", CompanySizeRange = "51-200", Status = TenantStatus.Active };
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var otherTenant = new Tenant { Id = Guid.NewGuid(), Name = "Other Tenant", Slug = "checklist-templates-rls-other", CompanySizeRange = "1-50", Status = TenantStatus.Active };
        _otherTenantId = otherTenant.Id;
        db.Tenants.Add(otherTenant);

        var legalEntity = new LegalEntity { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Acme Co" };
        _legalEntityId = legalEntity.Id;
        db.LegalEntities.Add(legalEntity);

        var department = new Department { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Engineering", Code = "ENG", IsActive = true };
        _departmentId = department.Id;
        db.Departments.Add(department);

        var position = new Position { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, DepartmentId = _departmentId, Name = "Software Engineer", PositionType = Position.TypeUnique, MaxOccupancy = 1, IsActive = true };
        _positionId = position.Id;
        db.Positions.Add(position);

        await db.SaveChangesAsync();

        await CreateRestrictedRoleAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Create_List_Get_Update_Archive_RoundTrip_ThroughRestrictedRole()
    {
        await using var db = CreateContext(useRestrictedRole: true);
        var repository = new EfChecklistTemplateRepository(db);
        var template = new Domain.Features.CoreHr.Entities.ChecklistTemplate
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Standard Onboarding", TemplateType = "onboarding",
            LegalEntityId = _legalEntityId, TasksJson = "[]", IsActive = true,
        };
        await repository.AddAsync(template);
        await repository.SaveChangesAsync();

        (await repository.GetByIdAsync(_tenantId, template.Id)).Should().NotBeNull();

        var (items, totalCount) = await repository.ListAsync(_tenantId, new ChecklistTemplateListFilter(null, _legalEntityId, null, null, false), 1, 25);
        totalCount.Should().Be(1);
        items.Should().ContainSingle(x => x.Id == template.Id);

        var tracked = await repository.GetTrackedByIdAsync(_tenantId, template.Id);
        tracked!.Name = "Renamed";
        await repository.SaveChangesAsync();
        (await repository.GetByIdAsync(_tenantId, template.Id))!.Name.Should().Be("Renamed");

        var trackedForArchive = await repository.GetTrackedByIdAsync(_tenantId, template.Id);
        trackedForArchive!.IsActive = false;
        await repository.SaveChangesAsync();
        (await repository.GetByIdAsync(_tenantId, template.Id))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_DoesNotReturnAnotherTenantsTemplate_WhenQueriedThroughRestrictedRole()
    {
        await using var seedDb = CreateContext();
        var template = new Domain.Features.CoreHr.Entities.ChecklistTemplate
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tenant-Owned", TemplateType = "onboarding",
            LegalEntityId = _legalEntityId, TasksJson = "[]", IsActive = true,
        };
        seedDb.ChecklistTemplates.Add(template);
        await seedDb.SaveChangesAsync();

        // A restricted-role connection whose tenant context resolves to a *different* tenant
        // must never see this row, even when it passes the correct tenant id explicitly to the
        // repository call - RLS enforces this at the database layer regardless of what the
        // application-layer WHERE clause says.
        await using var otherTenantDb = CreateContext(useRestrictedRole: true, tenantOverride: _otherTenantId);
        var repository = new EfChecklistTemplateRepository(otherTenantDb);

        (await repository.GetByIdAsync(_tenantId, template.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ListOnboardingMatchesAsync_OrdersPositionThenDepartmentThenCompany_AgainstRealPostgres()
    {
        await using var db = CreateContext(useRestrictedRole: true);
        var repository = new EfChecklistTemplateRepository(db);
        var company = new Domain.Features.CoreHr.Entities.ChecklistTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Company", TemplateType = "onboarding", LegalEntityId = _legalEntityId, TasksJson = "[]", IsActive = true };
        var dept = new Domain.Features.CoreHr.Entities.ChecklistTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Dept", TemplateType = "onboarding", LegalEntityId = _legalEntityId, DepartmentId = _departmentId, TasksJson = "[]", IsActive = true };
        var pos = new Domain.Features.CoreHr.Entities.ChecklistTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Pos", TemplateType = "onboarding", LegalEntityId = _legalEntityId, DepartmentId = _departmentId, PositionId = _positionId, TasksJson = "[]", IsActive = true };
        await repository.AddAsync(company); await repository.AddAsync(dept); await repository.AddAsync(pos);
        await repository.SaveChangesAsync();

        var matches = await repository.ListOnboardingMatchesAsync(_tenantId, _legalEntityId, _departmentId, _positionId);

        matches.Select(m => m.Template.Id).Should().Equal(pos.Id, dept.Id, company.Id);
    }

    [Fact]
    public async Task InstantiateAsync_CreatesRealEmployeeChecklistTaskRows_AndNeverMutatesTheTemplate()
    {
        await using var seedDb = CreateContext();
        var user = new User { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "newhire@checklist-templates-rls.onevo.dev", PasswordHash = "not-a-real-hash", FirstName = "New", LastName = "Hire", IsActive = false };
        seedDb.Users.Add(user);
        var employmentStatus = new EmploymentStatus { Id = 1, Code = "onboarding", Label = "Onboarding" };
        var employmentType = new EmploymentType { Id = 1, Code = "full_time", Label = "Full-Time" };
        var workMode = new WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true };
        if (!await seedDb.EmploymentStatuses.AnyAsync(x => x.Id == 1)) seedDb.EmploymentStatuses.Add(employmentStatus);
        if (!await seedDb.EmploymentTypes.AnyAsync(x => x.Id == 1)) seedDb.EmploymentTypes.Add(employmentType);
        if (!await seedDb.WorkModes.AnyAsync(x => x.Id == 1)) seedDb.WorkModes.Add(workMode);
        var employee = new Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = user.Id, EmployeeNumber = "EMP-INT-001", FirstName = "New", LastName = "Hire",
            Email = user.Email, LegalEntityId = _legalEntityId, DepartmentId = _departmentId, EmploymentStatusId = 1, EmploymentTypeId = 1, WorkModeId = 1,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        seedDb.Employees.Add(employee);
        const string originalTasksJson = "[{\"title\":\"Complete profile\",\"ownerType\":\"employee\",\"dueOffsetDays\":2,\"isRequired\":true}]";
        var template = new Domain.Features.CoreHr.Entities.ChecklistTemplate
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Starter", TemplateType = "onboarding",
            LegalEntityId = _legalEntityId, TasksJson = originalTasksJson, IsActive = true,
        };
        seedDb.ChecklistTemplates.Add(template);
        await seedDb.SaveChangesAsync();

        await using var db = CreateContext(useRestrictedRole: true);
        var repository = new EfEmployeeChecklistTaskRepository(db);
        var reloadedTemplate = await db.ChecklistTemplates.SingleAsync(t => t.Id == template.Id);

        var tasks = await repository.InstantiateAsync(reloadedTemplate, employee.Id, user.Id, editedTasksJson: null, new DateOnly(2026, 5, 1));
        await repository.SaveChangesAsync();

        tasks.Should().ContainSingle();
        var persisted = await db.EmployeeChecklistTasks.AsNoTracking().SingleAsync(t => t.EmployeeId == employee.Id);
        persisted.AssignedToId.Should().Be(user.Id);
        persisted.DueDate.Should().Be(new DateOnly(2026, 5, 3));
        persisted.IsRequired.Should().BeTrue();

        // TasksJson is stored as jsonb, so Postgres is free to reorder properties and change
        // whitespace on round-trip. Compare semantically through the same strict contract
        // parser the application uses, rather than asserting raw string equality.
        var reloadedTemplateAfter = await db.ChecklistTemplates.AsNoTracking().SingleAsync(t => t.Id == template.Id);
        var tasksAfter = ChecklistTaskJsonContract.Parse(reloadedTemplateAfter.TasksJson, ChecklistTaskDueRuleMode.OffsetDays);
        tasksAfter.Should().ContainSingle();
        var taskAfter = tasksAfter[0];
        taskAfter.Title.Should().Be("Complete profile");
        taskAfter.OwnerType.Should().Be(ChecklistTaskOwnerTypes.Employee);
        taskAfter.AssignedToId.Should().BeNull();
        taskAfter.DueOffsetDays.Should().Be(2);
        taskAfter.IsRequired.Should().BeTrue();
        taskAfter.DueDate.Should().BeNull();
    }

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
                GRANT SELECT, INSERT, UPDATE, DELETE ON checklist_templates, employee_checklist_tasks TO {RestrictedRoleName};
                GRANT SELECT ON tenants, legal_entities, departments, positions, employees, users,
                    employment_statuses, employment_types, work_modes TO {RestrictedRoleName};
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

    private ApplicationDbContext CreateContext(bool useRestrictedRole = false, Guid? tenantOverride = null)
    {
        var tenantContext = new TenantContextAccessor();
        if (useRestrictedRole)
        {
            tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(
                tenantOverride ?? _tenantId, "checklist-templates-rls", TenantStatus.Active, null));
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
