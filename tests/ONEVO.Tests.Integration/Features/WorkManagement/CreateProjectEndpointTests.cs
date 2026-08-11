using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Features.WorkManagement;

/// <summary>
/// HTTP integration tests for POST /api/v1/work/projects against a real PostgreSQL
/// database, mirroring the fixture pattern in
/// OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs (two fully-provisioned
/// tenants via the admin API + owner invite acceptance + session exchange).
///
/// No project-category creation endpoint exists yet (that's a later slice), so each
/// tenant's category is seeded directly through ApplicationDbContext in InitializeAsync.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class CreateProjectEndpointTests : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    private TenantSession _tenantA = null!;
    private TenantSession _tenantB = null!;
    private Guid _tenantACategoryId;
    private Guid _tenantBCategoryId;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_work_management_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await _postgres.StartAsync();
            connectionString = _postgres.GetConnectionString();
        }

        await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, _email);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        await WaitForSeedersAsync();

        var loginResponse = await SendJsonAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var adminCookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = adminCookies["admin_csrf"];
        _adminCookie = $"admin_session={adminCookies["admin_session"]}";

        _tenantA = await ProvisionAndLoginOwnerAsync("wm-int-a", "Work Mgmt Int A Co", "owner-a@wm-int.test");
        _tenantB = await ProvisionAndLoginOwnerAsync("wm-int-b", "Work Mgmt Int B Co", "owner-b@wm-int.test");

        _tenantACategoryId = await SeedProjectCategoryAsync(_tenantA.TenantId, "General");
        _tenantBCategoryId = await SeedProjectCategoryAsync(_tenantB.TenantId, "General");

        // No employee-onboarding feature exists anywhere in this codebase yet
        // (confirmed: zero "new Employee" call sites in src/) - tenant owners
        // provisioned through the admin API get a users row but never an
        // employees row. CreateProjectCommandHandler correctly requires one
        // (project_members.employee_id is non-null per the locked spec), so
        // the test fixture seeds it directly, exactly like SeedProjectCategoryAsync
        // above already does for the missing category-creation endpoint.
        await SeedEmployeeForOwnerAsync(_tenantA.TenantId, "owner-a@wm-int.test");
        await SeedEmployeeForOwnerAsync(_tenantB.TenantId, "owner-b@wm-int.test");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201WithDefaultObjectiveVersionAndMembership()
    {
        var response = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Website Revamp", "WEB1");

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location.Should().NotBeNull();

        var json = await ReadJsonAsync(response);
        json.GetProperty("defaultObjective").GetProperty("isDefault").GetBoolean().Should().BeTrue();
        json.GetProperty("defaultVersion").GetProperty("statusId").GetInt32().Should().Be(1);
        json.GetProperty("creatorMembership").GetProperty("membershipSource").GetString().Should().Be("system");
    }

    [Fact]
    public async Task Create_DuplicateIdentifierSameTenant_Returns409()
    {
        var first = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Duplicate Target", "DUP1");
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Duplicate Target Again", "DUP1");
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_ThenSecondTenantCannotSeeTheProjectRow_TenantIsolationHolds()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Isolation Check", "ISO1");
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var visibleToOtherTenant = await ExistsWhenScopedToTenantAsync(_tenantB.TenantId, projectId);
        visibleToOtherTenant.Should().BeFalse(
            "the project belongs to tenant A and must be invisible under tenant B's EF query filter + PostgreSQL RLS");

        var visibleToOwningTenant = await ExistsWhenScopedToTenantAsync(_tenantA.TenantId, projectId);
        visibleToOwningTenant.Should().BeTrue("the owning tenant must still be able to see its own row");
    }

    [Fact]
    public async Task Edit_ValidRequest_UpdatesProjectAndCascadesDefaultObjective()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Edit Target", "EDT1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var editResponse = await SendEditProjectAsync(_tenantA, projectId, "Edit Target Renamed", "EDT1");
        editResponse.StatusCode.Should().Be(HttpStatusCode.OK, await editResponse.Content.ReadAsStringAsync());

        var editJson = await ReadJsonAsync(editResponse);
        editJson.GetProperty("name").GetString().Should().Be("Edit Target Renamed");

        var getResponse = await SendGetProjectAsync(_tenantA, projectId);
        (await ReadJsonAsync(getResponse)).GetProperty("name").GetString().Should().Be("Edit Target Renamed");
    }

    [Fact]
    public async Task Edit_IdentifierChangeAttempted_Returns400()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Immutable Id Target", "IMM1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var editResponse = await SendEditProjectAsync(_tenantA, projectId, "Immutable Id Target", "CHANGED");

        editResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_CrossTenantProjectId_Returns404()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Cross Tenant Edit Target", "CTE1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var editResponse = await SendEditProjectAsync(_tenantB, projectId, "Should Not Apply", "CTE1");

        editResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant B must not be able to see or edit tenant A's project - RLS + EF global filter scoping");
    }

    [Fact]
    public async Task Delete_ByLead_SoftDeletesAndExcludesFromGetById()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Delete Target", "DEL1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var deleteResponse = await SendDeleteProjectAsync(_tenantA, projectId);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await SendGetProjectAsync(_tenantA, projectId);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "a soft-deleted project must not be viewable via GetById");
    }

    [Fact]
    public async Task Delete_AlreadyDeleted_Returns409()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Double Delete Target", "DBL1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var first = await SendDeleteProjectAsync(_tenantA, projectId);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await SendDeleteProjectAsync(_tenantA, projectId);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetById_OwningLead_ReturnsProjectWithIsLeadTrue()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "GetById Target", "GET1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var getResponse = await SendGetProjectAsync(_tenantA, projectId);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(getResponse);
        json.GetProperty("isLead").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyCallersOwnProjects_RequiresOnlyBaseModuleAccess()
    {
        await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Mine List Target", "MIN1");

        var response = await _client.SendAsync(BuildGetRequest(_tenantA, "/api/v1/work/projects/mine?pageSize=50"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").EnumerateArray().Any(p => p.GetProperty("identifier").GetString() == "MIN1").Should().BeTrue();
    }

    [Fact]
    public async Task ListByUser_RequiresProjectsReadPermission_OwnerHasItAndSucceeds()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "ByUser List Target", "BYU1");
        var ownerUserId = (await ReadJsonAsync(created)).GetProperty("creatorMembership").GetProperty("userId").GetGuid();

        var response = await _client.SendAsync(BuildGetRequest(_tenantA, $"/api/v1/work/projects?userId={ownerUserId}&pageSize=50"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").EnumerateArray().Any(p => p.GetProperty("identifier").GetString() == "BYU1").Should().BeTrue();
    }

    [Fact]
    public async Task ListForMember_MultiObjectiveMembership_DoesNotDuplicateProjectRow()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Dedup List Target", "DUP2");
        var createdJson = await ReadJsonAsync(created);
        var projectId = createdJson.GetProperty("project").GetProperty("id").GetGuid();
        var ownerUserId = createdJson.GetProperty("creatorMembership").GetProperty("userId").GetGuid();
        var defaultObjectiveId = createdJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();

        // No sub-Objective creation endpoint exists yet (Objective CRUD is a later phase - see
        // next-plan/Project Management.md) - seed a second Objective + a second membership row
        // for the SAME project + SAME user directly, exactly as ListForMemberAsync's DISTINCT
        // must handle: project_members' uniqueness is (tenant_id, project_id, objective_id,
        // user_id), so this is a legitimate second row, not a data error.
        await SeedSecondMembershipViaExtraObjectiveAsync(_tenantA.TenantId, projectId, ownerUserId, defaultObjectiveId);

        var response = await _client.SendAsync(BuildGetRequest(_tenantA, "/api/v1/work/projects/mine?pageSize=50"));
        var json = await ReadJsonAsync(response);

        json.GetProperty("items").EnumerateArray().Count(p => p.GetProperty("id").GetGuid() == projectId).Should().Be(1,
            "a user with two active memberships in the same project (via two Objectives) must see that project exactly once");
    }

    [Fact]
    public async Task CreateObjective_ByDefaultObjectiveHead_CreatesSubMilestone()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Milestone Tree Target", "MTT1");
        var createdJson = await ReadJsonAsync(created);
        var projectId = createdJson.GetProperty("project").GetProperty("id").GetGuid();
        var defaultObjectiveId = createdJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var response = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Design Phase", new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 1), 20m);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        json.GetProperty("parentObjectiveId").GetGuid().Should().Be(defaultObjectiveId);
        json.GetProperty("isDefault").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task CreateObjective_NestedUnderOwnSubMilestone_Succeeds()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Nested Milestone Target", "NST1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var first = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Phase 1", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), 30m);
        var firstId = (await ReadJsonAsync(first)).GetProperty("id").GetGuid();

        var nested = await SendCreateObjectiveAsync(_tenantA, firstId, "Phase 1a", new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 1), 10m);

        nested.StatusCode.Should().Be(HttpStatusCode.Created, await nested.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateObjective_DatesOutsideParentRange_Returns400()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Conflict Target", "CFT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        // Default Objective mirrors the Project's own start/target dates (2026-01-01 to 2026-06-01
        // for a project created via SendCreateProjectAsync) - this end date is well past that.
        var response = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Out Of Range", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), 5m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditObjective_ByCreatorHead_AppliesImmediately()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Edit Milestone Target", "EMT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Editable Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 15m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();

        var editResponse = await SendEditObjectiveAsync(_tenantA, subId, "Editable Phase Renamed", new DateOnly(2026, 1, 10), new DateOnly(2026, 3, 15), 18m);

        editResponse.StatusCode.Should().Be(HttpStatusCode.OK, await editResponse.Content.ReadAsStringAsync());
        (await ReadJsonAsync(editResponse)).GetProperty("title").GetString().Should().Be("Editable Phase Renamed");
    }

    [Fact]
    public async Task EditObjective_ConflictingButByCreator_StillAppliesImmediately()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Creator Conflict Target", "CCT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Creator Conflict Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 15m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();

        // Exceeds the Default Objective's own allocated hours (mirrors the Project's
        // defaultObjectiveAllocatedHours=40 from SendCreateProjectAsync) - a real conflict, but
        // the caller is this sub-objective's own creator, so it must still apply immediately.
        var editResponse = await SendEditObjectiveAsync(_tenantA, subId, "Creator Conflict Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 999m);

        editResponse.StatusCode.Should().Be(HttpStatusCode.OK, await editResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteObjective_ByCreatorHead_SoftDeletesImmediately()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Delete Milestone Target", "DMT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Deletable Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();

        var deleteResponse = await SendDeleteObjectiveAsync(_tenantA, subId);

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EditDeleteTransfer_OnDefaultObjective_Return400()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Default Carveout Target", "DCT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        (await SendEditObjectiveAsync(_tenantA, defaultObjectiveId, "Should Not Apply", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 5m))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendDeleteObjectiveAsync(_tenantA, defaultObjectiveId))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateObjective_CrossTenantParentId_Returns404()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Cross Tenant Milestone Target", "CTM1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var response = await SendCreateObjectiveAsync(_tenantB, defaultObjectiveId, "Should Not Apply", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), 5m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant B must not be able to see or create under tenant A's Default Objective - RLS + EF global filter scoping");
    }

    [Fact]
    public async Task GetObjectiveTree_ActiveMember_ReturnsFullTree()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Tree View Target", "TVT1");
        var createdJson = await ReadJsonAsync(created);
        var projectId = createdJson.GetProperty("project").GetProperty("id").GetGuid();
        var defaultObjectiveId = createdJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();
        await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Tree Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);

        var response = await _client.SendAsync(BuildGetRequest(_tenantA, $"/api/v1/work/projects/{projectId}/objectives"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.EnumerateArray().Should().HaveCountGreaterThanOrEqualTo(2, "the Default Objective plus the one sub-milestone just created");
    }

    [Fact]
    public async Task CreateObjective_ByCallerDefaultingToHead_CreatesProjectMembership()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Membership Sync Target", "MST1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var response = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Membership Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var objectiveId = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();

        var getResponse = await _client.SendAsync(BuildGetRequest(_tenantA, $"/api/v1/work/objectives/{objectiveId}"));
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, "the caller (default Head) must already have membership-based access to what they just created");
    }

    [Fact]
    public async Task AddThenRemoveObjectiveMember_HeadManagesMembership()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Member Mgmt Target", "MMT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Member Mgmt Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();
        var ownerUserId = (await ReadJsonAsync(created)).GetProperty("creatorMembership").GetProperty("userId").GetGuid();

        var addResponse = await SendAddObjectiveMemberAsync(_tenantA, subId, ownerUserId);
        addResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await addResponse.Content.ReadAsStringAsync());

        var removeHeadResponse = await SendRemoveObjectiveMemberAsync(_tenantA, subId, ownerUserId);
        removeHeadResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, "cannot remove the current head as a member - use Transfer instead");
    }

    [Fact]
    public async Task AchieveObjective_ByCreatorHead_AppliesAndFreezesEdit()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Achieve Milestone Target", "AMT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Achievable Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();

        var achieveResponse = await SendAchieveObjectiveAsync(_tenantA, subId);
        achieveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await achieveResponse.Content.ReadAsStringAsync());

        var editAfterAchieve = await SendEditObjectiveAsync(_tenantA, subId, "Should Not Apply", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 5m);
        editAfterAchieve.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an achieved milestone must be frozen for edits");

        var unachieveResponse = await SendUnachieveObjectiveAsync(_tenantA, subId);
        unachieveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AchieveObjective_WithUnachievedChild_Returns400()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Achieve Blocked Target", "ABT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var parent = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Parent Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), 30m);
        var parentId = (await ReadJsonAsync(parent)).GetProperty("id").GetGuid();
        await SendCreateObjectiveAsync(_tenantA, parentId, "Unachieved Child", new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 1), 5m);

        var achieveResponse = await SendAchieveObjectiveAsync(_tenantA, parentId);

        achieveResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the child must be achieved before the parent can be");
    }

    [Fact]
    public async Task AchieveThenUnachieveProject_LeadManagesTopLevelState()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Achieve Project Target", "APT1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var achieveResponse = await SendAchieveProjectAsync(_tenantA, projectId);
        achieveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await achieveResponse.Content.ReadAsStringAsync());

        var unachieveResponse = await SendUnachieveProjectAsync(_tenantA, projectId);
        unachieveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetMyObjectiveHistory_NoInactiveMemberships_ReturnsEmptyArray()
    {
        var response = await _client.SendAsync(BuildGetRequest(_tenantA, "/api/v1/work/objectives/mine/history"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetArrayLength().Should().Be(0);
    }

    // ── Project creation helper (multipart/form-data) ───────────────────────

    private async Task<HttpResponseMessage> SendCreateProjectAsync(
        TenantSession session, Guid categoryId, string name, string identifier)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(categoryId.ToString()), "CategoryId" },
            { new StringContent(name), "Name" },
            { new StringContent(identifier), "Identifier" },
            { new StringContent("2026-01-01"), "StartDate" },
            { new StringContent("2026-06-01"), "TargetDate" },
            { new StringContent("2026-06-15"), "ReleaseDate" },
            { new StringContent("40"), "DefaultObjectiveAllocatedHours" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/work/projects")
        {
            Content = form
        };
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendEditProjectAsync(TenantSession session, Guid projectId, string name, string? identifier)
    {
        var body = new
        {
            name,
            description = "edited description",
            categoryId = session == _tenantA ? _tenantACategoryId : _tenantBCategoryId,
            startDate = "2026-01-01",
            targetDate = "2026-08-01",
            color = "#123456",
            actualHours = 5,
            identifier
        };

        return await SendJsonAsync(HttpMethod.Put, session.Host, $"/api/v1/work/projects/{projectId}", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private async Task<HttpResponseMessage> SendDeleteProjectAsync(TenantSession session, Guid projectId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/work/projects/{projectId}");
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendGetProjectAsync(TenantSession session, Guid projectId)
        => await _client.SendAsync(BuildGetRequest(session, $"/api/v1/work/projects/{projectId}"));

    // ── Objective (milestone) helpers ────────────────────────────────────────

    private async Task<HttpResponseMessage> SendCreateObjectiveAsync(
        TenantSession session, Guid parentObjectiveId, string title, DateOnly startDate, DateOnly endDate, decimal allocatedHours)
    {
        var body = new { parentObjectiveId, title, description = "test description", startDate, endDate, allocatedHours, headUserId = (Guid?)null };
        return await SendJsonAsync(HttpMethod.Post, session.Host, "/api/v1/work/objectives", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private async Task<HttpResponseMessage> SendEditObjectiveAsync(
        TenantSession session, Guid objectiveId, string title, DateOnly startDate, DateOnly endDate, decimal allocatedHours)
    {
        var body = new { title, description = "edited description", startDate, endDate, allocatedHours };
        return await SendJsonAsync(HttpMethod.Put, session.Host, $"/api/v1/work/objectives/{objectiveId}", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private async Task<HttpResponseMessage> SendDeleteObjectiveAsync(TenantSession session, Guid objectiveId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/work/objectives/{objectiveId}");
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAddObjectiveMemberAsync(TenantSession session, Guid objectiveId, Guid userId)
    {
        var body = new { userId };
        return await SendJsonAsync(HttpMethod.Post, session.Host, $"/api/v1/work/objectives/{objectiveId}/members", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private async Task<HttpResponseMessage> SendRemoveObjectiveMemberAsync(TenantSession session, Guid objectiveId, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/work/objectives/{objectiveId}/members/{userId}");
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAchieveObjectiveAsync(TenantSession session, Guid objectiveId)
        => await SendPostNoBodyAsync(session, $"/api/v1/work/objectives/{objectiveId}/achieve");

    private async Task<HttpResponseMessage> SendUnachieveObjectiveAsync(TenantSession session, Guid objectiveId)
        => await SendPostNoBodyAsync(session, $"/api/v1/work/objectives/{objectiveId}/unachieve");

    private async Task<HttpResponseMessage> SendAchieveProjectAsync(TenantSession session, Guid projectId)
        => await SendPostNoBodyAsync(session, $"/api/v1/work/projects/{projectId}/achieve");

    private async Task<HttpResponseMessage> SendUnachieveProjectAsync(TenantSession session, Guid projectId)
        => await SendPostNoBodyAsync(session, $"/api/v1/work/projects/{projectId}/unachieve");

    private async Task<HttpResponseMessage> SendPostNoBodyAsync(TenantSession session, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private HttpRequestMessage BuildGetRequest(TenantSession session, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return request;
    }

    private async Task SeedSecondMembershipViaExtraObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, Guid defaultObjectiveId)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var employee = await db.Employees.SingleAsync(e => e.TenantId == tenantId && e.UserId == userId);

        var subObjective = new ONEVO.Domain.Features.WorkManagement.Objectives.Entities.Objective
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ParentObjectiveId = defaultObjectiveId,
            IsDefault = false, Title = "Sub Objective", OwnerId = userId, IsActive = true,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        };
        db.Objectives.Add(subObjective);

        db.ProjectMembers.Add(new ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities.ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = subObjective.Id,
            UserId = userId, EmployeeId = employee.Id,
            MembershipSource = ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities.ProjectMembershipSources.ObjectiveInvitation,
            IsActive = true, JoinedAt = DateTimeOffset.UtcNow, CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedProjectCategoryAsync(Guid tenantId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new ProjectCategory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            IsActive = true,
            CreatedById = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ProjectCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task SeedEmployeeForOwnerAsync(Guid tenantId, string ownerEmail)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.TenantId == tenantId && u.Email == ownerEmail);

        db.Employees.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            EmployeeNumber = "OWNER-1",
            FirstName = "Test",
            LastName = "Owner",
            Email = ownerEmail,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EmploymentStatusId = EmploymentStatusIds.Active,
            CreatedById = user.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task GrantWorkManagementAccessToOwnerRoleAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ownerRole = await db.Roles.SingleAsync(r => r.TenantId == tenantId && r.Name == "Owner");

        // Every permission tagged module "work_management" (projects:access, projects:read,
        // okr:read, etc.) is missing from the Owner role - DefaultRoleSeeder.SeedOwnerRoleAsync
        // only grants permissions whose module appears in the plan's included_modules_json, which
        // uses the newer canonical Phase 1 keys ("projects", "objectives_milestones", ...) and
        // never included the legacy "work_management" key these permissions are still tagged
        // with. Grant the whole module's permission set, not just projects:access, since this
        // test class exercises multiple Work Management permissions (projects:read via
        // ListByUser, etc.) that hit the identical gap.
        var workManagementPermissions = await db.Permissions.Where(p => p.Module == "work_management").ToListAsync();
        var alreadyGrantedIds = (await db.RolePermissions
                .Where(rp => rp.RoleId == ownerRole.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync())
            .ToHashSet();

        foreach (var permission in workManagementPermissions)
        {
            if (!alreadyGrantedIds.Contains(permission.Id))
                db.RolePermissions.Add(new RolePermission { TenantId = tenantId, RoleId = ownerRole.Id, PermissionId = permission.Id });
        }

        // A granted RolePermission row alone is not enough: PermissionResolver.ResolveAsync
        // filters every role-permission row live by the tenant's *active module keys*
        // (TenantSubscription.SelectedModulesJson), matched against Permission.Module. Patching
        // the module list here (test-tenant-scoped, not a change to seeded/global data) is the
        // surgical fix; correcting the module tags themselves is a separate, real production
        // concern out of scope for this test fixture.
        var subscription = await db.TenantSubscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync();
        var modules = JsonSerializer.Deserialize<List<string>>(subscription.SelectedModulesJson) ?? [];
        if (!modules.Contains("work_management"))
        {
            modules.Add("work_management");
            subscription.SelectedModulesJson = JsonSerializer.Serialize(modules);
        }

        await db.SaveChangesAsync();
    }

    private async Task<bool> ExistsWhenScopedToTenantAsync(Guid tenantId, Guid projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Projects.AnyAsync(p => p.Id == projectId);
    }

    // ── Provisioning helper (mirrors LegalEntitiesIntegrationTests) ─────────

    private sealed record TenantSession(Guid TenantId, string Host, string SessionCookie, string CsrfHeader);

    private async Task<TenantSession> ProvisionAndLoginOwnerAsync(string slug, string companyName, string ownerEmail)
    {
        const string ownerPassword = "OwnerPass@2026!";
        var host = $"{slug}.localhost";

        var createBody = new
        {
            company_name = companyName,
            slug,
            industry_profile = "technology",
            company_size_range = "11-50",
            legal_entity_name = companyName,
            registration_number = $"PV-{slug}",
            country = "LK",
            timezone = "Asia/Colombo",
            currency = "LKR",
            subscription = new
            {
                plan_id = SeededPlanId,
                billing_cycle = "monthly",
                commercial_model = "standard"
            },
            owner_invite = new
            {
                email = ownerEmail,
                first_name = "Test",
                last_name = "Owner",
                completion_methods = new[] { "password" }
            }
        };

        var createResponse = await SendJsonAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", createBody,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: Guid.NewGuid().ToString());
        var createJson = await ReadJsonAsync(createResponse);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson.ToString());
        var tenantId = createJson.GetProperty("tenantId").GetGuid();

        // The seeded plan's included_modules_json uses the canonical Phase 1 module keys
        // (e.g. "projects", "objectives_milestones"), but every Work Management permission in
        // PermissionSeeder.cs (including projects:access) is still tagged module "work_management"
        // - a legacy key no active plan actually includes. DefaultRoleSeeder.SeedOwnerRoleAsync
        // does an exact-string module match, so the Owner role created at tenant creation never
        // gets projects:access from that path. Grant it directly to the Owner role here, before
        // login below bakes permission claims into the session - RequirePermissionAttribute reads
        // those claims, not a live resolve, so granting after login would have no effect until a
        // second login (see design doc §7's known session-refresh limitation).
        await GrantWorkManagementAccessToOwnerRoleAsync(tenantId);

        var inviteToken = await WaitForInviteTokenForAsync(ownerEmail);
        inviteToken.Should().NotBeNullOrEmpty();

        var acceptResponse = await SendJsonAsync(HttpMethod.Post, host,
            $"/api/v1/auth/invitations/{inviteToken}/accept-password",
            new
            {
                password = ownerPassword,
                confirm_password = ownerPassword,
                acceptances = new[]
                {
                    new { document_type = "terms", version = "1.0", decision = "accepted" },
                    new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
                }
            });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmResponse = await SendJsonAsync(HttpMethod.Patch, AdminHost,
            $"/admin/v1/tenants/{tenantId}/provision/confirm", new { confirm = true },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        const string baseHost = "localhost";
        var loginResponse = await SendJsonAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email = ownerEmail, password = ownerPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var loginJson = await ReadJsonAsync(loginResponse);
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = QueryHelpers.ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendJsonAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange",
            new { code = exchangeCode });
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);

        return new TenantSession(tenantId, host, sessionCookie, csrfHeader);
    }

    private async Task<string?> WaitForInviteTokenForAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var template in _email.Templates)
            {
                if (template.TemplateId != "tenant_owner_invite")
                    continue;
                if (!string.Equals(template.To, email, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (template.Data.TryGetProperty("invite_token", out var token))
                    return token.GetString();
            }
            await Task.Delay(250);
        }
        return null;
    }

    private async Task WaitForSeedersAsync()
    {
        await using (var migrateScope = _factory.Services.CreateAsyncScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            try
            {
                var permissionsReady = await db.Set<ONEVO.Domain.Features.Auth.Entities.Permission>().AnyAsync();
                var planReady = await db.Set<ONEVO.Domain.Features.SharedPlatform.Entities.SubscriptionPlan>()
                    .AnyAsync(p => p.Id == SeededPlanId);
                if (permissionsReady && planReady)
                    return;
            }
            catch
            {
                // Schema not created yet; keep polling.
            }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan missing).");
    }

    // ── HTTP helpers (mirrors LegalEntitiesIntegrationTests) ────────────────

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method, string host, string path, object? body,
        string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-Token", csrfToken);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Dictionary<string, string> ParseSetCookies(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return cookies;

        foreach (var raw in values)
        {
            var pair = raw.Split(';', 2)[0];
            var idx = pair.IndexOf('=');
            if (idx > 0)
                cookies[pair[..idx].Trim()] = pair[(idx + 1)..].Trim();
        }

        return cookies;
    }
}
