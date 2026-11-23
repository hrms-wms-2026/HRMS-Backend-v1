using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

/// <summary>
/// Exercises EfTaskDraftRepository against real PostgreSQL through a NOBYPASSRLS role, so the
/// task_drafts tenant_isolation policy is genuinely in force. Requires Docker.
/// </summary>
public sealed class TaskDraftRepositoryIntegrationTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "crownus_app";
    private const string RestrictedRolePassword = PrivilegedRoleTestBootstrap.AppRolePassword;

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantId;
    private Guid _otherTenantId;
    private Guid _ownerUserId;
    private Guid _otherUserId;
    private Guid _projectId;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();

        await using var db = CreateContext(tenantId: null);
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Task Draft Tenant", Slug = "task-drafts-a", CompanySizeRange = "51-200", Status = TenantStatus.Active };
        var other = new Tenant { Id = Guid.NewGuid(), Name = "Task Draft Other Tenant", Slug = "task-drafts-b", CompanySizeRange = "51-200", Status = TenantStatus.Active };
        _tenantId = tenant.Id;
        _otherTenantId = other.Id;
        db.Tenants.AddRange(tenant, other);
        await db.SaveChangesAsync();

        await SeedOwnersAndProjectAsync();
        await CreateRestrictedRoleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Add_ListOwned_GetOwned_Remove_RoundTrip()
    {
        var owner = _ownerUserId;
        var mine = NewDraft(owner, _projectId, "Fix login", "{\"priority\":\"high\"}");
        var theirs = NewDraft(_otherUserId, _projectId, "Other", "{}");

        await using (var db = CreateContext(_tenantId, restricted: true))
        {
            var repo = new EfTaskDraftRepository(db);
            await repo.AddAsync(mine);
            await repo.AddAsync(theirs);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(_tenantId, restricted: true))
        {
            var repo = new EfTaskDraftRepository(db);

            (await repo.ListOwnedAsync(_tenantId, owner)).Select(d => d.Id).Should().Equal(mine.Id);
            (await repo.GetOwnedAsync(_tenantId, owner, theirs.Id)).Should().BeNull("another user's draft is invisible");

            var loaded = await repo.GetOwnedAsync(_tenantId, owner, mine.Id);
            loaded.Should().NotBeNull();
            // jsonb normalises whitespace
            loaded!.PayloadJson.Replace(" ", "").Should().Be("{\"priority\":\"high\"}");

            repo.Remove(loaded);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(_tenantId, restricted: true))
        {
            (await new EfTaskDraftRepository(db).ListOwnedAsync(_tenantId, owner)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task RowLevelSecurity_HidesDraftsOfOtherTenants()
    {
        var draft = NewDraft(_ownerUserId, _projectId, "Tenant A only", "{}");

        await using (var db = CreateContext(_tenantId, restricted: true))
        {
            await new EfTaskDraftRepository(db).AddAsync(draft);
            await db.SaveChangesAsync();
        }

        await using var otherTenantDb = CreateContext(_otherTenantId, restricted: true);
        var visible = await otherTenantDb.TaskDrafts.AsNoTracking().CountAsync();
        visible.Should().Be(0, "tenant_isolation must hide tenant A's drafts from tenant B");
    }

    private TaskDraft NewDraft(Guid owner, Guid project, string title, string payload) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        OwnerUserId = owner,
        ProjectId = project,
        Title = title,
        PayloadJson = payload,
        CreatedById = owner,
    };

    /// <summary>
    /// Real owners and a real project: task_drafts.owner_user_id and task_drafts.project_id have foreign keys
    /// to users and projects since task 39b.
    /// </summary>
    private async Task SeedOwnersAndProjectAsync()
    {
        await using var db = CreateContext(_tenantId);
        _ownerUserId = TestUsers.New(db, _tenantId, "Draft", "Owner");
        _otherUserId = TestUsers.New(db, _tenantId, "Other", "Owner");
        var legalEntity = new ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Task Draft Co",
            Timezone = "UTC", WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0),
            StandardWorkingDays = "[1,2,3,4,5]", BreakDurationMinutes = 60,
        };
        db.LegalEntities.Add(legalEntity);
        var lead = new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _ownerUserId, LegalEntityId = legalEntity.Id,
            EmployeeNumber = "TD-0001", FirstName = "Draft", LastName = "Owner", Email = "draft.owner@task-drafts.test",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), EmploymentStatusId = 1,
        };
        db.Employees.Add(lead);
        var category = new ONEVO.Domain.Features.WorkManagement.Projects.Entities.ProjectCategory
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Drafts", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ONEVO.Domain.Features.WorkManagement.Projects.Entities.ProjectCategory>().Add(category);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var project = new ONEVO.Domain.Features.WorkManagement.Projects.Entities.Project
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, OwningLegalEntityId = legalEntity.Id, CategoryId = category.Id,
            Name = "Draft Project", Identifier = "DRAFT", LeadId = lead.Id,
            StartDate = today.AddMonths(-1), TargetDate = today.AddMonths(3), IsActive = true,
        };
        db.Projects.Add(project);
        _projectId = project.Id;
        await db.SaveChangesAsync();
    }

    private async Task CreateRestrictedRoleAsync()
    {
        await using var connection = TestDb.CreateConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await command.ExecuteNonQueryAsync();

        _restrictedConnectionString = new SqlConnectionStringBuilder(_connectionString)
        {
            UserID = RestrictedRoleName,
            Password = RestrictedRolePassword
        }.ConnectionString;
    }

    private ApplicationDbContext CreateContext(Guid? tenantId, bool restricted = false)
    {
        var tenantContext = new TenantContextAccessor();
        if (tenantId is { } id)
        {
            tenantContext.Resolve(new TenantRegistryEntry(id, "task-drafts", TenantStatus.Active, null));
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseTestSqlServer(restricted ? _restrictedConnectionString : _connectionString)
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
