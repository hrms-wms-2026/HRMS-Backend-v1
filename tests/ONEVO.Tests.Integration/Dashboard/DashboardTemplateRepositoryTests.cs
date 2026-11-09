using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Dashboard.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Dashboard;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.Dashboard;

/// <summary>One cloned, fully migrated SQL Server database shared by the facts of the class.</summary>
public sealed class DashboardTemplateRepositoryTestsFixture : IAsyncLifetime
{
    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync() => _connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>System mode (no tenant filter) unless a tenant is given, then tenant mode with the RLS interceptor.</summary>
    public ApplicationDbContext CreateContext(Guid? tenantId = null)
    {
        var tenantContext = new TenantContextAccessor();
        if (tenantId is not null)
            tenantContext.Resolve(new TenantRegistryEntry(tenantId.Value, $"t-{tenantId:N}", TenantStatus.Active, null));

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

/// <summary>
/// EfDashboardTemplateRepository and the template table constraints against real SQL Server (Docker): the
/// is_no_role_template CHECK, the one-No-role-per-tenant partial unique index, and tenant scoping.
/// </summary>
public sealed class DashboardTemplateRepositoryTests : IClassFixture<DashboardTemplateRepositoryTestsFixture>
{
    private const string Key = DashboardKeys.MyWork;
    private readonly DashboardTemplateRepositoryTestsFixture _fixture;

    public DashboardTemplateRepositoryTests(DashboardTemplateRepositoryTestsFixture fixture) => _fixture = fixture;

    private async Task<(Guid TenantId, Guid RoleA, Guid RoleB)> SeedTenantAsync()
    {
        var tenantId = Guid.NewGuid();
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        await using var db = _fixture.CreateContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Template tenant", Slug = $"tpl-{tenantId:N}",
            CompanySizeRange = "51-200", Status = TenantStatus.Active,
        });
        db.Roles.AddRange(
            new Role { Id = roleA, TenantId = tenantId, Name = "Manager" },
            new Role { Id = roleB, TenantId = tenantId, Name = "Employee" });
        await db.SaveChangesAsync();
        return (tenantId, roleA, roleB);
    }

    private static DashboardTemplate Template(Guid tenantId, Guid? roleId, string key = Key, bool noRole = false,
        bool active = true, params string[] widgetKeys)
    {
        var template = new DashboardTemplate
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DashboardKey = key, RoleId = roleId,
            IsNoRoleTemplate = noRole, Name = noRole ? "No role" : "Role", IsActive = active,
        };
        var y = 0;
        foreach (var widgetKey in widgetKeys)
        {
            var widget = new DashboardTemplateWidget
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TemplateId = template.Id, WidgetKey = widgetKey,
            };
            widget.Layouts.Add(new DashboardTemplateWidgetLayout
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TemplateWidgetId = widget.Id,
                Breakpoint = Breakpoints.Desktop, X = 0, Y = y, W = 4, H = 3,
            });
            y += 3;
            template.Widgets.Add(widget);
        }

        return template;
    }

    private async Task AddAsync(params DashboardTemplate[] templates)
    {
        await using var db = _fixture.CreateContext();
        db.DashboardTemplates.AddRange(templates);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListForRolesAsync_returns_only_active_templates_of_given_roles_and_key()
    {
        var (tenantId, roleA, roleB) = await SeedTenantAsync();
        var a = Template(tenantId, roleA, widgetKeys: ["today-at-a-glance", "quick-actions"]);
        var inactive = Template(tenantId, roleB, active: false, widgetKeys: ["quick-actions"]);
        var otherKey = Template(tenantId, roleA, key: DashboardKeys.MyTeam, widgetKeys: ["team-status"]);
        var noRole = Template(tenantId, null, noRole: true, widgetKeys: ["quick-actions"]);
        await AddAsync(a, inactive, otherKey, noRole);

        // A soft-deleted child widget is excluded.
        await using (var db = _fixture.CreateContext())
        {
            var widget = await db.DashboardTemplateWidgets.SingleAsync(w => w.TemplateId == a.Id && w.WidgetKey == "quick-actions");
            db.DashboardTemplateWidgets.Remove(widget);
            await db.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext(tenantId);
        var result = await new EfDashboardTemplateRepository(read).ListForRolesAsync(tenantId, Key, [roleA, roleB]);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(a.Id);
        result[0].Widgets.Select(w => w.WidgetKey).Should().Equal("today-at-a-glance");
        result[0].Widgets[0].Layouts.Should().ContainSingle(l => l.Breakpoint == Breakpoints.Desktop);
    }

    [Fact]
    public async Task GetNoRoleAsync_returns_flagged_template_not_null_role_rows()
    {
        var (tenantId, roleA, _) = await SeedTenantAsync();
        var role = Template(tenantId, roleA, widgetKeys: ["quick-actions"]);
        var noRole = Template(tenantId, null, noRole: true, widgetKeys: ["today-at-a-glance"]);
        await AddAsync(role, noRole);

        await using var read = _fixture.CreateContext(tenantId);
        var repo = new EfDashboardTemplateRepository(read);
        var result = await repo.GetNoRoleAsync(tenantId, Key);

        result.Should().NotBeNull();
        result!.Id.Should().Be(noRole.Id);
        result.IsNoRoleTemplate.Should().BeTrue();
        result.Widgets.Select(w => w.WidgetKey).Should().Equal("today-at-a-glance");

        (await repo.GetNoRoleAsync(tenantId, DashboardKeys.MyTeam)).Should().BeNull();
    }

    [Fact]
    public async Task Second_no_role_template_for_same_tenant_and_key_violates_unique_index()
    {
        var (tenantId, _, _) = await SeedTenantAsync();
        await AddAsync(Template(tenantId, null, noRole: true));

        var act = () => AddAsync(Template(tenantId, null, noRole: true));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Template_with_role_and_no_role_flag_violates_check_constraint()
    {
        var (tenantId, roleA, _) = await SeedTenantAsync();

        var both = () => AddAsync(Template(tenantId, roleA, noRole: true));
        var neither = () => AddAsync(Template(tenantId, null, noRole: false));

        await both.Should().ThrowAsync<DbUpdateException>();
        await neither.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Tenant_context_B_cannot_read_tenant_A_templates()
    {
        var (tenantA, roleA, _) = await SeedTenantAsync();
        var (tenantB, _, _) = await SeedTenantAsync();
        await AddAsync(Template(tenantA, roleA, widgetKeys: ["quick-actions"]), Template(tenantA, null, noRole: true));

        await using var asB = _fixture.CreateContext(tenantB);
        var repo = new EfDashboardTemplateRepository(asB);

        (await repo.ListForRolesAsync(tenantA, Key, [roleA])).Should().BeEmpty();
        (await repo.GetNoRoleAsync(tenantA, Key)).Should().BeNull();
    }
}
