using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.OrgStructure.Department;

/// <summary>
/// Real HTTPS/API validation for Department Part 2D: every request in this class goes through
/// the full Kestrel TestServer pipeline (Authorize, RequirePermission, MediatR, EF/Postgres/RLS,
/// CSRF middleware) against a real PostgreSQL database - not controller/handler unit tests.
/// Mirrors the LegalEntitiesIntegrationTests convention (two provisioned tenants for cross-tenant
/// isolation). The org:read-only and no-permission fixture users are seeded directly via the DB
/// (there is no public "invite additional employee" endpoint on this tenant's own API yet - only
/// the single owner-invite issued during tenant creation) and then logged in through the real
/// base-domain login -> session-exchange flow, including their own LegalAcceptanceRecord rows so
/// that login completes without a legal challenge (mirroring what invite-acceptance writes for the
/// owner).
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class DepartmentsIntegrationTests : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private const string FixtureUserPassword = "Password123!";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    private TenantSession _tenantAOwner = null!;
    private TenantSession _tenantBOwner = null!;
    private TenantSession _tenantAOrgReadOnly = null!;
    private TenantSession _tenantANoAccess = null!;
    private Guid _tenantAId;
    private Guid _tenantALegalEntityId;
    private Guid _tenantASecondLegalEntityId;
    private Guid _tenantBLegalEntityId;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_departments_test")
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

        var loginResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var adminCookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = adminCookies["admin_csrf"];
        _adminCookie = $"admin_session={adminCookies["admin_session"]}";

        _tenantAOwner = await ProvisionAndLoginOwnerAsync("dept-a", "Dept A Co", "owner-a@dept.test");
        _tenantBOwner = await ProvisionAndLoginOwnerAsync("dept-b", "Dept B Co", "owner-b@dept.test");

        _tenantAId = await GetTenantIdAsync(_tenantAOwner.Host);
        _tenantALegalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantAOwner);
        _tenantBLegalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantBOwner);
        _tenantASecondLegalEntityId = await CreateSecondLegalEntityAsync(_tenantAOwner);

        _tenantAOrgReadOnly = await SeedAndLoginFixtureUserAsync(
            _tenantAId, _tenantAOwner.Host, "org-reader@dept-a.test", permissionCodes: ["org:read"], roleName: "Org Reader");
        _tenantANoAccess = await SeedAndLoginFixtureUserAsync(
            _tenantAId, _tenantAOwner.Host, "no-access@dept-a.test", permissionCodes: [], roleName: "No Access");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    // -- Auth/permission matrix --------------------------------------------

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithOrgRead_Returns200()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_WithoutOrgRead_Returns403()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            body: null, cookie: _tenantANoAccess.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_WithoutOrgRead_Returns403()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Get Perm Dept");

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            body: null, cookie: _tenantANoAccess.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Should Be Blocked Dept" },
            cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Update Perm Dept");

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            new { name = "Renamed" },
            cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Delete Perm Dept");

        var response = await SendAsync(HttpMethod.Delete, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithOrgManage_Returns201()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Full Access Create Dept" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // -- CRUD + business rules (Owner, full org:manage) ---------------------

    [Fact]
    public async Task Create_Get_Update_Delete_FullLifecycle()
    {
        var created = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Lifecycle Dept");
        created.GetProperty("name").GetString().Should().Be("Lifecycle Dept");
        created.TryGetProperty("headPositionId", out var headOnCreate).Should().BeTrue();
        headOnCreate.ValueKind.Should().Be(JsonValueKind.Null);

        var id = created.GetProperty("id").GetGuid();

        var get = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var update = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            new { name = "Lifecycle Dept Renamed" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateJson = await ReadJsonAsync(update);
        updateJson.GetProperty("name").GetString().Should().Be("Lifecycle Dept Renamed");

        var delete = await SendAsync(HttpMethod.Delete, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Soft delete only: the row still resolves by id, just IsActive = false.
        var afterDelete = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        afterDelete.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDeleteJson = await ReadJsonAsync(afterDelete);
        afterDeleteJson.GetProperty("isActive").GetBoolean().Should().BeFalse();

        // Excluded by default, included only with includeInactive=true.
        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task Create_DuplicateNameInSameLegalEntity_Returns409()
    {
        await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Duplicate Dept Name");

        var duplicate = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Duplicate Dept Name" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SameNameInDifferentLegalEntity_IsAllowed()
    {
        await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Shared Name Dept");

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments",
            new { name = "Shared Name Dept" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_SelfParenting_Returns400()
    {
        // UpdateDepartmentCommandValidator (FluentValidation, runs in the MediatR pipeline
        // before the handler) already rejects ParentDepartmentId == DepartmentId with a
        // validation failure -> 400. UpdateDepartmentCommandHandler.cs:49-50 has its own
        // self-parenting check returning Conflict (409), but the validator's earlier rejection
        // means that handler-level check is unreachable for this exact input - both layers
        // reject self-parenting, the validator's 400 is just the one that actually surfaces.
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Self Parent Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            new { name = "Self Parent Dept", parentDepartmentId = id },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ParentInDifferentLegalEntity_Returns404()
    {
        var parentInOtherLegalEntity = await CreateDepartmentAsync(
            _tenantAOwner, _tenantASecondLegalEntityId, "Parent In Other LE");
        var parentId = parentInOtherLegalEntity.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Child With Wrong Parent LE", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ParentInDifferentTenant_Returns404()
    {
        var parentInOtherTenant = await CreateDepartmentAsync(
            _tenantBOwner, _tenantBLegalEntityId, "Parent In Other Tenant");
        var parentId = parentInOtherTenant.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Child With Cross Tenant Parent", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_WithHeadPositionId_Returns409_AssignmentDeferredToUpdate()
    {
        // Part 3: a new department has no positions belonging to it yet, so head-position
        // assignment on create is rejected outright (not silently ignored) - see
        // DEPARTMENT_HEAD_POSITION_ASSIGNMENT_REPORT.md. Assign it afterwards through update.
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Head Position Deferred Dept", headPositionId = Guid.NewGuid() },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // -- Part 3: department head position assignment (update-only) ----------

    [Fact]
    public async Task Update_WithHeadPositionId_AssignsHeadPosition_AndResponseIncludesIt()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Head Assign Dept");
        var departmentId = department.GetProperty("id").GetGuid();
        var position = await CreatePositionAsync(_tenantAId, _tenantALegalEntityId, departmentId, isActive: true);

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Assign Dept", headPositionId = position.Id },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("headPositionId").GetGuid().Should().Be(position.Id);

        var get = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}");
        get.GetProperty("headPositionId").GetGuid().Should().Be(position.Id);
    }

    [Fact]
    public async Task Update_OmittingHeadPositionId_ClearsPreviouslyAssignedHead()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Head Clear Dept");
        var departmentId = department.GetProperty("id").GetGuid();
        var position = await CreatePositionAsync(_tenantAId, _tenantALegalEntityId, departmentId, isActive: true);

        var assign = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Clear Dept", headPositionId = position.Id },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        assign.StatusCode.Should().Be(HttpStatusCode.OK);

        // Full-replace PUT semantics: omitting headPositionId clears it, exactly like sending
        // null would - the request model cannot distinguish the two (see report for rationale).
        var clear = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Clear Dept" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(clear);
        json.GetProperty("headPositionId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Update_HeadPositionId_NotFound_Returns404()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Head Missing Dept");
        var departmentId = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Missing Dept", headPositionId = Guid.NewGuid() },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_HeadPositionId_Inactive_Returns409()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Head Inactive Dept");
        var departmentId = department.GetProperty("id").GetGuid();
        var position = await CreatePositionAsync(_tenantAId, _tenantALegalEntityId, departmentId, isActive: false);

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Inactive Dept", headPositionId = position.Id },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_HeadPositionId_FromAnotherDepartment_Returns409()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Head Wrong Dept A");
        var departmentId = department.GetProperty("id").GetGuid();
        var otherDepartment = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Head Wrong Dept B");
        var otherDepartmentId = otherDepartment.GetProperty("id").GetGuid();
        var position = await CreatePositionAsync(_tenantAId, _tenantALegalEntityId, otherDepartmentId, isActive: true);

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Wrong Dept A", headPositionId = position.Id },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_HeadPositionId_FromAnotherLegalEntity_Returns404()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Head Cross LE Dept");
        var departmentId = department.GetProperty("id").GetGuid();
        var deptInOtherLe = await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, "Head Cross LE Other Dept");
        var positionInOtherLe = await CreatePositionAsync(
            _tenantAId, _tenantASecondLegalEntityId, deptInOtherLe.GetProperty("id").GetGuid(), isActive: true);

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Cross LE Dept", headPositionId = positionInOtherLe.Id },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_HeadPositionId_FromAnotherTenant_Returns404_RlsIsolationIntact()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Head Cross Tenant Dept");
        var departmentId = department.GetProperty("id").GetGuid();

        // Seeded entirely within tenant B (its own tenantId and legalEntityId). Note this does
        // not isolate tenant scoping from legal-entity scoping - either filter alone would
        // explain the 404, since both belong to tenant B here. Isolating them would require a
        // position row whose tenant_id and legal_entity_id belong to different tenants; that
        // combination was not verified against PositionConfiguration's FK constraints and was
        // deliberately not attempted (see Remaining limitations in the report).
        var tenantBId = await GetTenantIdAsync(_tenantBOwner.Host);
        var positionInOtherTenant = await CreatePositionAsync(
            tenantBId, _tenantBLegalEntityId, departmentId: null, isActive: true);

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Cross Tenant Dept", headPositionId = positionInOtherTenant.Id },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Code rules + hierarchy safety + archive route -----------------------

    [Fact]
    public async Task Create_WithCode_Returns201_AndCodeIsPreserved()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Operations Dept", code = "OPS" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.GetProperty("code").GetString().Should().Be("OPS");
    }

    [Fact]
    public async Task Create_DuplicateCodeCaseInsensitiveInSameLegalEntity_Returns409()
    {
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Original Code Dept", code = "DUPCODE" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Different Name Dept", code = "dupcode" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SameCodeInDifferentLegalEntity_IsAllowed()
    {
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Shared Code Dept A", code = "SHARED" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments",
            new { name = "Shared Code Dept B", code = "SHARED" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_InvalidCodeCharacters_Returns400()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Bad Code Dept", code = "bad code!" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ParentIsInactive_Returns409()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Inactive Parent Dept");
        var parentId = parent.GetProperty("id").GetGuid();
        var archiveResponse = await SendAsync(HttpMethod.Delete, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var child = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Child Of Inactive Parent");
        var childId = child.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{childId}",
            new { name = "Child Of Inactive Parent", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ParentIsDescendant_Returns409()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Cycle Parent Dept");
        var parentId = parent.GetProperty("id").GetGuid();

        var childResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Cycle Child Dept", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        childResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var childJson = await ReadJsonAsync(childResponse);
        var childId = childJson.GetProperty("id").GetGuid();

        // Attempt to make the parent report to its own child - must be blocked as a cycle.
        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}",
            new { name = "Cycle Parent Dept", parentDepartmentId = childId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Route_SoftDeactivates_AndListExcludesByDefault()
    {
        var created = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Route Dept");
        var id = created.GetProperty("id").GetGuid();

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var getJson = await ReadJsonAsync(get);
        getJson.GetProperty("isActive").GetBoolean().Should().BeFalse();

        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    // -- Part 3: search, sort, pagination, tree ------------------------------

    [Fact]
    public async Task List_ReturnsOnlyDepartmentsForSelectedLegalEntity()
    {
        var deptInFirstLe = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "List Isolation LE1");
        var deptInSecondLe = await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, "List Isolation LE2");

        var firstLeList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        var ids = firstLeList.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(deptInFirstLe.GetProperty("id").GetGuid());
        ids.Should().NotContain(deptInSecondLe.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_Search_ReturnsOnlyMatchingDepartments_ScopedToLegalEntity()
    {
        await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Search Match Marketing");
        await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Search NoMatch Finance");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?search=marketing");

        var names = response.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();
        names.Should().Contain("Search Match Marketing");
        names.Should().NotContain("Search NoMatch Finance");
    }

    [Fact]
    public async Task List_Pagination_ReturnsCorrectTotalCountAndPageItems()
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, $"Page Dept {i}");
        }

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments?page=1&pageSize=2");

        response.GetProperty("totalCount").GetInt32().Should().Be(3);
        response.GetProperty("page").GetInt32().Should().Be(1);
        response.GetProperty("pageSize").GetInt32().Should().Be(2);
        response.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task List_TreeView_ReturnsHierarchyForSelectedLegalEntityOnly()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Tree Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Tree Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, "Other LE Root");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?view=tree");

        response.TryGetProperty("treeItems", out var treeItems).Should().BeTrue();
        var parentNode = treeItems.EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == parentId);
        parentNode.GetProperty("children").GetArrayLength().Should().Be(1);
        treeItems.EnumerateArray().Select(n => n.GetProperty("name").GetString()).Should().NotContain("Other LE Root");
    }

    [Fact]
    public async Task List_TreeView_DoesNotExposeTenantId()
    {
        await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Tree No Tenant");

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?view=tree",
            body: null, cookie: _tenantAOwner.SessionCookie);
        var text = await response.Content.ReadAsStringAsync();

        text.Should().NotContain("tenantId", "tree responses must not expose the tenant id");
    }

    [Fact]
    public async Task List_InvalidSortBy_Returns400()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?sortBy=nope",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_PageSizeOverMax_Returns400()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?pageSize=101",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_ParentDepartmentIdFilter_ReturnsOnlyDirectChildren()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, "Filter Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        var childResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments",
            new { name = "Filter Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var childId = (await ReadJsonAsync(childResponse)).GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments",
            new { name = "Filter Grandchild", parentDepartmentId = childId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments?parentDepartmentId={parentId}");

        var names = response.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();
        names.Should().ContainSingle().Which.Should().Be("Filter Child");
    }

    // -- Cross-tenant / cross-legal-entity isolation -------------------------
    // 404 is the correct "blocked" semantic here (existence-hiding), matching
    // the same convention already established by LegalEntitiesIntegrationTests
    // (GetGeneralSettings_OutOfTenantId_Returns404) - not a weakened check.

    [Fact]
    public async Task Get_CrossTenant_Returns404()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Cross Tenant Dept");

        var response = await SendAsync(HttpMethod.Get, _tenantBOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            body: null, cookie: _tenantBOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_CrossTenant_LegalEntityId_Returns404()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantBOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            body: null, cookie: _tenantBOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_CrossLegalEntity_WithinSameTenant_Returns404()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "LE Scoped Dept");

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Fixture provisioning helpers -----------------------------------------

    private sealed record TenantSession(string Host, string SessionCookie, string CsrfHeader);

    [Fact]
    public async Task ArchiveCheck_Unauthenticated_Returns401()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Check Unauth Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive-check", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Restore_Unauthenticated_Returns401()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Restore Unauth Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/restore", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ArchiveCheck_Eligible_ReturnsCanArchiveTrue()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Check Eligible");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("canArchive").GetBoolean().Should().BeTrue();
        json.GetProperty("blockers").GetProperty("activeSubdepartmentCount").GetInt32().Should().Be(0);
        json.GetProperty("blockers").GetProperty("activeEmployeeCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ArchiveCheck_WithOrgRead_Returns200()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Check Perm Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive-check",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ArchiveCheck_Blocked_ReturnsAccurateCounts_WhenActiveChildExists()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Check Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Archive Check Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("canArchive").GetBoolean().Should().BeFalse();
        json.GetProperty("blockers").GetProperty("activeSubdepartmentCount").GetInt32().Should().Be(1);
        json.GetProperty("blockers").GetProperty("isUsedAsParent").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Archive_Blocked_WhenActiveChildExists_Returns409_AndDoesNotDeactivate()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Blocked Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Archive Blocked Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var afterArchive = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        var afterArchiveJson = await ReadJsonAsync(afterArchive);
        afterArchiveJson.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Delete_Blocked_WhenActiveChildExists_Returns409()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Delete Blocked Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Delete Blocked Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var delete = await SendAsync(HttpMethod.Delete, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Child_WithNoBlockers_Succeeds_ThenRestore_Succeeds()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Then Restore");
        var id = department.GetProperty("id").GetGuid();

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restore = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/restore",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        var getJson = await ReadJsonAsync(get);
        getJson.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Restore_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Restore Perm Dept");
        var id = department.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/restore",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Restore_Fails_WhenParentIsArchived()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Restore Parent Archived");
        var parentId = parent.GetProperty("id").GetGuid();
        var child = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Restore Child Blocked", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var childJson = await ReadJsonAsync(child);
        var childId = childJson.GetProperty("id").GetGuid();

        // Archive child first (no blockers), then the parent (which now has zero active
        // children, so it archives cleanly too).
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{childId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var archiveParent = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archiveParent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restoreChild = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{childId}/restore",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        restoreChild.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Blocked_WhenActiveEmployeeExists()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Has Active Employee");
        var departmentId = department.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var activeStatus = await db.EmploymentStatuses.SingleAsync(s => s.Code == "active");

            db.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                UserId = Guid.NewGuid(),
                LegalEntityId = _tenantALegalEntityId,
                DepartmentId = departmentId,
                EmployeeNumber = $"E{Guid.NewGuid():N}"[..12],
                FirstName = "Active",
                LastName = "Employee",
                Email = $"{Guid.NewGuid():N}@dept.test",
                EmploymentStatusId = activeStatus.Id,
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
            await db.SaveChangesAsync();
        }

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var check = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var checkJson = await ReadJsonAsync(check);
        checkJson.GetProperty("blockers").GetProperty("activeEmployeeCount").GetInt32().Should().Be(1);
        checkJson.GetProperty("blockers").GetProperty("hasActiveEmployees").GetBoolean().Should().BeTrue();
    }

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

        var createResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", createBody,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: Guid.NewGuid().ToString());
        var createJson = await ReadJsonAsync(createResponse);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson.ToString());
        var tenantId = createJson.GetProperty("tenantId").GetGuid();

        var inviteToken = await WaitForInviteTokenForAsync(ownerEmail);
        inviteToken.Should().NotBeNullOrEmpty();

        var acceptResponse = await SendAsync(HttpMethod.Post, host,
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

        var confirmResponse = await SendAsync(HttpMethod.Patch, AdminHost,
            $"/admin/v1/tenants/{tenantId}/provision/confirm", new { confirm = true },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return await LoginViaBaseHostAsync(host, ownerEmail, ownerPassword);
    }

    private async Task<TenantSession> LoginViaBaseHostAsync(string host, string email, string password)
    {
        const string baseHost = "localhost";
        var loginResponse = await SendAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email, password });
        var loginJson = await ReadJsonAsync(loginResponse);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, loginJson.ToString());
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange",
            new { code = exchangeCode });
        var exchangeJson = await ReadJsonAsync(exchangeResponse);
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, exchangeJson.ToString());
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);
        return new TenantSession(host, sessionCookie, csrfHeader);
    }

    /// <summary>
    /// Seeds a second tenant user directly in the DB with a dedicated role carrying exactly
    /// <paramref name="permissionCodes"/> (empty = zero permissions, mirroring the auto-created,
    /// unassigned "Employee" role every tenant already gets from DefaultRoleSeeder), plus the
    /// LegalAcceptanceRecord rows real invite-acceptance would have written, so the subsequent
    /// real HTTP base-login -> session-exchange completes cleanly instead of hitting a legal
    /// challenge. Only the fixture setup bypasses HTTP; every assertion in this class still runs
    /// the real request through the full pipeline.
    /// </summary>
    private async Task<TenantSession> SeedAndLoginFixtureUserAsync(
        Guid tenantId, string host, string email, IReadOnlyList<string> permissionCodes, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;

        var userId = Guid.NewGuid();
        db.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Email = email,
            FirstName = "Fixture",
            LastName = roleName,
            PasswordHash = hasher.Hash(FixtureUserPassword),
            IsActive = true,
            EmailVerified = true,
            MustChangePassword = false,
            PasswordSetByAdmin = false,
            CreatedAt = now,
            CreatedById = userId
        });

        var roleId = Guid.NewGuid();
        db.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = roleName,
            Description = $"Part 2D fixture role: {roleName}",
            IsSystem = false,
            CreatedAt = now,
            CreatedById = userId
        });

        foreach (var code in permissionCodes)
        {
            var permission = await db.Permissions.SingleAsync(p => p.Code == code);
            db.Add(new RolePermission { TenantId = tenantId, RoleId = roleId, PermissionId = permission.Id });
        }

        db.Add(new UserRole { TenantId = tenantId, UserId = userId, RoleId = roleId, AssignedAt = now, AssignedBy = userId });

        db.Add(new LegalAcceptanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            DocumentType = "terms", DocumentVersion = "1.0", Decision = "accepted",
            Required = true, DecidedAt = now, Source = "test-seed"
        });
        db.Add(new LegalAcceptanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            DocumentType = "privacy_notice", DocumentVersion = "1.0", Decision = "acknowledged",
            Required = true, DecidedAt = now, Source = "test-seed"
        });

        await db.SaveChangesAsync();

        return await LoginViaBaseHostAsync(host, email, FixtureUserPassword);
    }

    private async Task<Guid> GetTenantIdAsync(string host)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var slug = host.Split('.')[0];
        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Slug == slug);
        return tenant.Id;
    }

    private async Task<Guid> GetPrimaryLegalEntityIdAsync(TenantSession session)
    {
        var list = await GetJsonAsync(session, "/api/v1/org/legal-entities");
        var primary = list.EnumerateArray().Single(i => i.GetProperty("isPrimary").GetBoolean());
        return primary.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateSecondLegalEntityAsync(TenantSession session)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host, "/api/v1/org/legal-entities",
            new
            {
                name = "Dept A Second Co",
                companyCode = "DEPTA2",
                registrationNumber = "REG-DEPTA2",
                countryCode = "LKA",
                currencyCode = "LKR"
            },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateDepartmentAsync(TenantSession session, Guid legalEntityId, string name)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host,
            $"/api/v1/org/legal-entities/{legalEntityId}/departments", new { name },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadJsonAsync(response);
    }

    private sealed record SeededPosition(Guid Id);

    /// <summary>
    /// Seeds a position directly via the DbContext (there is no public Positions HTTP contract
    /// exercised elsewhere in this fixture), mirroring the Employee-seeding block above - it
    /// already works under FORCE ROW LEVEL SECURITY in this same test class.
    /// </summary>
    private async Task<SeededPosition> CreatePositionAsync(
        Guid tenantId, Guid legalEntityId, Guid? departmentId, bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var position = new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DepartmentId = departmentId,
            Name = $"Head Position {Guid.NewGuid():N}",
            PositionType = ONEVO.Domain.Features.OrgStructure.Entities.Position.TypeUnique,
            MaxOccupancy = 1,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = Guid.NewGuid()
        };
        db.Add(position);
        await db.SaveChangesAsync();

        return new SeededPosition(position.Id);
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
                var permissionsReady = await db.Set<Permission>().AnyAsync();
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

    // -- HTTP helpers (mirrors TenantProvisioningE2ETests/LegalEntitiesIntegrationTests) --------

    private async Task<HttpResponseMessage> SendAsync(
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

    private async Task<JsonElement> GetJsonAsync(TenantSession session, string path)
    {
        var response = await SendAsync(HttpMethod.Get, session.Host, path, body: null, cookie: session.SessionCookie);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, json.ToString());
        return json;
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
