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
    private const string RestrictedRoleName = "task_drafts_rls_test_role";
    private const string RestrictedRolePassword = "task-drafts-rls-test-role-password";

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantId;
    private Guid _otherTenantId;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();

        await using var db = CreateContext(tenantId: null);
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Task Draft Tenant", Slug = "task-drafts-a", CompanySizeRange = "51-200", Status = TenantStatus.Active };
        var other = new Tenant { Id = Guid.NewGuid(), Name = "Task Draft Other Tenant", Slug = "task-drafts-b", CompanySizeRange = "51-200", Status = TenantStatus.Active };
        _tenantId = tenant.Id;
        _otherTenantId = other.Id;
        db.Tenants.AddRange(tenant, other);
        await db.SaveChangesAsync();

        await CreateRestrictedRoleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Add_ListOwned_GetOwned_Remove_RoundTrip()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var project = Guid.NewGuid();
        var mine = NewDraft(owner, project, "Fix login", "{\"priority\":\"high\"}");
        var theirs = NewDraft(other, project, "Other", "{}");

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
        var owner = Guid.NewGuid();
        var draft = NewDraft(owner, Guid.NewGuid(), "Tenant A only", "{}");

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

    private async Task CreateRestrictedRoleAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{RestrictedRoleName}') THEN
                    CREATE ROLE {RestrictedRoleName}
                        LOGIN PASSWORD '{RestrictedRolePassword}' NOSUPERUSER NOBYPASSRLS;
                END IF;
            END
            $$;
            GRANT USAGE ON SCHEMA public TO {RestrictedRoleName};
            GRANT SELECT, INSERT, UPDATE, DELETE ON task_drafts TO {RestrictedRoleName};
            GRANT SELECT ON tenants TO {RestrictedRoleName};
        ";
        await command.ExecuteNonQueryAsync();

        _restrictedConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString)
        {
            Username = RestrictedRoleName,
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
            .UseNpgsql(restricted ? _restrictedConnectionString : _connectionString)
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
