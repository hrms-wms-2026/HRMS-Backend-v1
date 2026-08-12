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

namespace ONEVO.Tests.Integration.OrgStructure.Position;

/// <summary>
/// Real HTTPS/API validation for Position Part 2D: every request in this class goes through
/// the full Kestrel TestServer pipeline (Authorize, RequirePermission, MediatR, EF/Postgres/RLS,
/// CSRF middleware) against a real PostgreSQL database - not controller/handler unit tests.
/// Mirrors the DepartmentsIntegrationTests/LegalEntitiesIntegrationTests convention (two
/// provisioned tenants for cross-tenant isolation). The org:read-only and no-permission fixture
/// users are seeded directly via the DB (there is no public "invite additional employee" endpoint
/// on this tenant's own API yet - only the single owner-invite issued during tenant creation) and
/// then logged in through the real base-domain login -> session-exchange flow, including their own
/// LegalAcceptanceRecord rows so that login completes without a legal challenge.
///
/// Department.HeadPositionId is seeded directly via DbContext for the head-of-department archive
/// blocker scenario. As of Department Part 3, CreateDepartmentRequest/UpdateDepartmentRequest do
/// expose headPositionId as writable (Update only - see DEPARTMENT_HEAD_POSITION_ASSIGNMENT_REPORT.md);
/// this file still seeds directly because going through the full department-update flow (which
/// itself requires the head position to already belong to the target department) is unnecessary
/// setup for scenarios that only care about the archive blocker. Create/UpdatePositionRequest -
/// the *Position* request contracts - never expose headPositionId, by design (verified by
/// PositionsControllerArchitectureTests). position_assignments now exists (added in Part 2E),
/// but ArchiveCheck_* still assert the documented unsupported/null CurrentOccupancy shape -
/// that pair was deliberately left unpopulated (see PositionListItemResponse for why); occupant-
/// preview coverage for List/Tree (assignedCount/occupantPreview/remainingAssignedCount) lives in
/// ONEVO.Tests.Unit (ListPositionsQueryHandlerTests, GetPositionTreeQueryHandlerTests,
/// PositionMapperTests, EfPositionAssignmentRepositoryTests) rather than here.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class PositionsIntegrationTests : IAsyncLifetime
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
    private Guid _tenantADepartmentId;
    private Guid _tenantASecondLegalEntityDepartmentId;
    private Guid _tenantBDepartmentId;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_positions_test")
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

        _tenantAOwner = await ProvisionAndLoginOwnerAsync("pos-a", "Position A Co", "owner-a@pos.test");
        _tenantBOwner = await ProvisionAndLoginOwnerAsync("pos-b", "Position B Co", "owner-b@pos.test");

        _tenantAId = await GetTenantIdAsync(_tenantAOwner.Host);
        _tenantALegalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantAOwner);
        _tenantBLegalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantBOwner);
        _tenantASecondLegalEntityId = await CreateSecondLegalEntityAsync(_tenantAOwner);

        _tenantAOrgReadOnly = await SeedAndLoginFixtureUserAsync(
            _tenantAId, _tenantAOwner.Host, "org-reader@pos-a.test", permissionCodes: ["org:read"], roleName: "Org Reader");
        _tenantANoAccess = await SeedAndLoginFixtureUserAsync(
            _tenantAId, _tenantAOwner.Host, "no-access@pos-a.test", permissionCodes: [], roleName: "No Access");

        _tenantADepartmentId = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Position Fixture Dept A1");
        _tenantASecondLegalEntityDepartmentId = await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, "Position Fixture Dept A2");
        _tenantBDepartmentId = await CreateDepartmentAsync(_tenantBOwner, _tenantBLegalEntityId, "Position Fixture Dept B1");
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
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithoutOrgRead_Returns403()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            body: null, cookie: _tenantANoAccess.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithOrgRead_Returns200()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantADepartmentId, name = "Blocked Position", code = "PERMB", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithOrgManage_Returns201()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantADepartmentId, name = "Full Access Create Position", code = "PERMO", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Update Perm Position", "UPDPM");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}",
            new { departmentId = _tenantADepartmentId, name = "Renamed", code = "UPDPM", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Archive_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Perm Position", "ARCHP");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/archive",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Restore_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Restore Perm Position", "RESTP");
        var id = position.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/restore",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ArchiveCheck_Unauthenticated_Returns401()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Check Unauth Position", "ACHKU");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/archive-check", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Restore_Unauthenticated_Returns401()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Restore Unauth Position", "RESTU");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/restore", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -- Create -----------------------------------------------------------

    [Fact]
    public async Task Create_ValidBody_Returns201_WithExpectedShape()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantADepartmentId, name = "Shape Position", code = "SHAPE", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;

        json.GetProperty("id").ValueKind.Should().Be(JsonValueKind.String);
        json.GetProperty("legalEntityId").GetGuid().Should().Be(_tenantALegalEntityId);
        json.GetProperty("departmentId").GetGuid().Should().Be(_tenantADepartmentId);
        json.GetProperty("name").GetString().Should().Be("Shape Position");
        json.GetProperty("code").GetString().Should().Be("SHAPE");
        json.GetProperty("positionType").GetString().Should().Be("unique");
        json.GetProperty("maxOccupancy").GetInt32().Should().Be(1);
        json.GetProperty("reportsToPositionId").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("isActive").GetBoolean().Should().BeTrue();

        text.Should().NotContain("tenantId", "the position response must not expose the raw tenant id");
        text.Should().NotContain("headPositionId", "position responses have no headPositionId concept");
    }

    [Fact]
    public async Task Create_RequestBody_ExtraTenantIdField_IsIgnored()
    {
        // CreatePositionRequest has no TenantId property; System.Text.Json model binding
        // silently drops unknown JSON members, so this can never be used to bypass RLS.
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantADepartmentId, name = "Tenant Id Ignored Position", code = "TIDIG", maxOccupancy = 1, reportsToPositionId = (Guid?)null, tenantId = Guid.NewGuid() },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.GetProperty("legalEntityId").GetGuid().Should().Be(_tenantALegalEntityId);
    }

    [Fact]
    public async Task Create_DuplicateCodeCaseInsensitiveInSameLegalEntity_Returns409()
    {
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Original Code Position", "DUPCD");

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantADepartmentId, name = "Different Name Position", code = "dupcd", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SameCodeInDifferentLegalEntity_IsAllowed()
    {
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Shared Code Position A", "SHRDP");

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/positions",
            new { departmentId = _tenantASecondLegalEntityDepartmentId, name = "Shared Code Position B", code = "SHRDP", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_DepartmentFromAnotherLegalEntity_Returns404()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantASecondLegalEntityDepartmentId, name = "Wrong LE Dept Position", code = "WRNGL", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_DepartmentFromAnotherTenant_Returns404()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantBDepartmentId, name = "Cross Tenant Dept Position", code = "XTDPT", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_MaxOccupancyZero_Returns400()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantADepartmentId, name = "Invalid Capacity Position", code = "BADCP", maxOccupancy = 0, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_CodeLongerThanFiveCharacters_Returns400()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantADepartmentId, name = "Invalid Code Length Position", code = "TOOLONG", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -- List ---------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsPaginatedShape()
    {
        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions");

        response.TryGetProperty("items", out _).Should().BeTrue();
        response.TryGetProperty("page", out _).Should().BeTrue();
        response.TryGetProperty("pageSize", out _).Should().BeTrue();
        response.TryGetProperty("totalCount", out _).Should().BeTrue();
        response.TryGetProperty("totalPages", out _).Should().BeTrue();
    }

    [Fact]
    public async Task List_ReturnsOnlyPositionsForSelectedLegalEntity()
    {
        var posInFirstLe = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "List Isolation LE1 Position", "ISOL1");
        var posInSecondLe = await CreatePositionAsync(_tenantAOwner, _tenantASecondLegalEntityId, _tenantASecondLegalEntityDepartmentId, "List Isolation LE2 Position", "ISOL2");

        var firstLeList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions");
        var ids = firstLeList.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(posInFirstLe.GetProperty("id").GetGuid());
        ids.Should().NotContain(posInSecondLe.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_FiltersByDepartmentId()
    {
        var otherDept = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "List Filter Other Dept");
        var inTarget = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Dept Filter Target Position", "DFT-1");
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, otherDept, "Dept Filter Other Position", "DFT-2");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions?departmentId={_tenantADepartmentId}");

        var ids = response.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        ids.Should().Contain(inTarget.GetProperty("id").GetGuid());
        response.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("departmentId").GetGuid())
            .Should().OnlyContain(id => id == _tenantADepartmentId);
    }

    [Fact]
    public async Task List_Search_FindsByName()
    {
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Search Match Marketing Lead", "SRCN1");
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Search NoMatch Finance Lead", "SRCN2");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions?search=marketing");

        var names = response.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        names.Should().Contain("Search Match Marketing Lead");
        names.Should().NotContain("Search NoMatch Finance Lead");
    }

    [Fact]
    public async Task List_Search_FindsByCode()
    {
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Search Code Match Position", "SRCHM");
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Search Code NoMatch Position", "OTHRC");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions?search=SRCH");

        var codes = response.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()).ToList();
        codes.Should().Contain("SRCHM");
        codes.Should().NotContain("OTHRC");
    }

    [Fact]
    public async Task List_IncludeInactiveFalse_ExcludesArchived_IncludeInactiveTrue_IncludesArchived()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archived List Position", "ARCHL");
        var id = position.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions?includeInactive=true");
        inclusiveList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task List_SortByName_Ascending_OrdersAlphabetically()
    {
        await CreatePositionAsync(_tenantAOwner, _tenantASecondLegalEntityId, _tenantASecondLegalEntityDepartmentId, "Zebra Sort Position", "SORTZ");
        await CreatePositionAsync(_tenantAOwner, _tenantASecondLegalEntityId, _tenantASecondLegalEntityDepartmentId, "Alpha Sort Position", "SORTA");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/positions?sortBy=name&sortDirection=asc");

        var names = response.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();
        var alphaIndex = names.IndexOf("Alpha Sort Position");
        var zebraIndex = names.IndexOf("Zebra Sort Position");
        alphaIndex.Should().BeLessThan(zebraIndex);
    }

    [Fact]
    public async Task List_SortByCode_Descending_Orders()
    {
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Sort Code Low Position", "AAAAA");
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Sort Code High Position", "ZZZZZ");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions?sortBy=code&sortDirection=desc&pageSize=100");

        var codes = response.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToList();
        var lowIndex = codes.IndexOf("AAAAA");
        var highIndex = codes.IndexOf("ZZZZZ");
        highIndex.Should().BeLessThan(lowIndex);
    }

    // -- Get ------------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsCreatedPosition()
    {
        var created = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Get Target Position", "GET-1");
        var id = created.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("id").GetGuid().Should().Be(id);
        json.GetProperty("name").GetString().Should().Be("Get Target Position");
    }

    [Fact]
    public async Task Get_CrossLegalEntity_WithinSameTenant_Returns404()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "LE Scoped Position", "LESCP");

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/positions/{position.GetProperty("id").GetGuid()}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Tree -------------------------------------------------------------------

    [Fact]
    public async Task Tree_ReturnsRootAndChild_ReportsToHierarchy()
    {
        var root = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Tree Root Position", "TREER");
        var rootId = root.GetProperty("id").GetGuid();
        var child = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Tree Child Position", "TREEC",
            maxOccupancy: 3, reportsToPositionId: rootId);
        var childId = child.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/tree",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);

        json.ValueKind.Should().Be(JsonValueKind.Array);
        var rootNode = json.EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == rootId);
        var childNode = rootNode.GetProperty("children").EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == childId);
        childNode.GetProperty("reportsToPositionId").GetGuid().Should().Be(rootId);
    }

    [Fact]
    public async Task Tree_ExcludesCrossLegalEntityPositions()
    {
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Tree LE1 Root Position", "TREL1");
        await CreatePositionAsync(_tenantAOwner, _tenantASecondLegalEntityId, _tenantASecondLegalEntityDepartmentId, "Tree LE2 Root Position", "TREL2");

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/tree",
            body: null, cookie: _tenantAOwner.SessionCookie);
        var json = await ReadJsonAsync(response);

        json.EnumerateArray().Select(n => n.GetProperty("name").GetString()).Should().NotContain("Tree LE2 Root Position");
    }

    // -- Update -----------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesFields()
    {
        var otherDept = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Update Target Dept");
        var manager = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Update Manager Position", "UPDMG");
        var managerId = manager.GetProperty("id").GetGuid();
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Update Lifecycle Position", "UPDLC");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}",
            new { departmentId = otherDept, name = "Update Lifecycle Position Renamed", code = "UPDL2", maxOccupancy = 4, reportsToPositionId = managerId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("name").GetString().Should().Be("Update Lifecycle Position Renamed");
        json.GetProperty("code").GetString().Should().Be("UPDL2");
        json.GetProperty("positionType").GetString().Should().Be("pooled");
        json.GetProperty("maxOccupancy").GetInt32().Should().Be(4);
        json.GetProperty("departmentId").GetGuid().Should().Be(otherDept);
        json.GetProperty("reportsToPositionId").GetGuid().Should().Be(managerId);
    }

    [Fact]
    public async Task Update_SelfReporting_Returns400()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Self Report Position", "SELFR");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}",
            new { departmentId = _tenantADepartmentId, name = "Self Report Position", code = "SELFR", maxOccupancy = 1, reportsToPositionId = id },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ReportingCycle_Returns422()
    {
        var parent = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Cycle Parent Position", "CYCPR");
        var parentId = parent.GetProperty("id").GetGuid();
        var child = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Cycle Child Position", "CYCCH",
            reportsToPositionId: parentId);
        var childId = child.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{parentId}",
            new { departmentId = _tenantADepartmentId, name = "Cycle Parent Position", code = "CYCPR", maxOccupancy = 1, reportsToPositionId = childId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Update_DepartmentFromAnotherLegalEntity_Returns404()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Update Wrong LE Dept Position", "UPDWD");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}",
            new { departmentId = _tenantASecondLegalEntityDepartmentId, name = "Update Wrong LE Dept Position", code = "UPDWD", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ReportsToFromAnotherLegalEntity_Returns404()
    {
        var otherLePosition = await CreatePositionAsync(_tenantAOwner, _tenantASecondLegalEntityId, _tenantASecondLegalEntityDepartmentId, "Other LE Reports To Position", "OTHLR");
        var otherLePositionId = otherLePosition.GetProperty("id").GetGuid();
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Update Wrong LE ReportsTo Position", "UPDWR");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}",
            new { departmentId = _tenantADepartmentId, name = "Update Wrong LE ReportsTo Position", code = "UPDWR", maxOccupancy = 1, reportsToPositionId = otherLePositionId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Archive-check ------------------------------------------------------------

    [Fact]
    public async Task ArchiveCheck_Eligible_ReturnsCanArchiveTrue_AndUnsupportedOccupantCount()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Check Eligible Position", "ACHKO");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("activeOccupants").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("activeOccupantsCheckSupported").GetBoolean().Should().BeFalse();
        json.GetProperty("headOfDepartments").GetInt32().Should().Be(0);
        json.GetProperty("activeChildPositions").GetInt32().Should().Be(0);
        json.GetProperty("canArchive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveCheck_WithOrgRead_Returns200()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Check Perm Position", "ACHKP");
        var id = position.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/archive-check",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ArchiveCheck_ActiveChildExists_ReturnsAccurateCount_CanArchiveFalse()
    {
        var parent = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Check Parent Position", "ACHPR");
        var parentId = parent.GetProperty("id").GetGuid();
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Check Child Position", "ACHKC",
            reportsToPositionId: parentId);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{parentId}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("activeChildPositions").GetInt32().Should().Be(1);
        json.GetProperty("canArchive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ArchiveCheck_UsedAsDepartmentHead_ReturnsHeadOfDepartmentsGreaterThanZero()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Department Head Candidate Position", "ACHKH");
        var positionId = position.GetProperty("id").GetGuid();
        var headedDeptId = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Headed By Position Dept");
        await SetDepartmentHeadPositionAsync(headedDeptId, positionId);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{positionId}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("headOfDepartments").GetInt32().Should().BeGreaterThan(0);
        json.GetProperty("canArchive").GetBoolean().Should().BeFalse();
    }

    // -- Archive ------------------------------------------------------------------

    [Fact]
    public async Task Archive_NoBlockers_Returns204_SetsIsActiveFalse_AndAffectsListVisibility()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Lifecycle Position", "ARCLC");
        var id = position.GetProperty("id").GetGuid();

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        var getJson = await ReadJsonAsync(get);
        getJson.GetProperty("isActive").GetBoolean().Should().BeFalse();

        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);
    }

    [Fact]
    public async Task Archive_BlockedByActiveChild_Returns409_DoesNotDeactivate_ChildNotReparented()
    {
        var parent = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Blocked Parent Position", "ARBLP");
        var parentId = parent.GetProperty("id").GetGuid();
        var child = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Blocked Child Position", "ARBLC",
            reportsToPositionId: parentId);
        var childId = child.GetProperty("id").GetGuid();

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{parentId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var afterArchive = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{parentId}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        (await ReadJsonAsync(afterArchive)).GetProperty("isActive").GetBoolean().Should().BeTrue();

        var childAfter = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{childId}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        (await ReadJsonAsync(childAfter)).GetProperty("reportsToPositionId").GetGuid().Should().Be(parentId);
    }

    [Fact]
    public async Task Archive_BlockedByDepartmentHead_Returns409()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Archive Blocked Head Position", "ARBLH");
        var positionId = position.GetProperty("id").GetGuid();
        var headedDeptId = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Blocked Headed Dept");
        await SetDepartmentHeadPositionAsync(headedDeptId, positionId);

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{positionId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // -- Restore ------------------------------------------------------------------

    [Fact]
    public async Task Restore_Archived_Returns204_SetsIsActiveTrue()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Restore Lifecycle Position", "RESLC");
        var id = position.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var restore = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/restore",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        (await ReadJsonAsync(get)).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Restore_AlreadyActive_IsIdempotent_Returns204()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Restore Idempotent Position", "RESID");
        var id = position.GetProperty("id").GetGuid();

        var restore = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{id}/restore",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Restore_BlockedWhenDepartmentInactive_Returns422()
    {
        var deptId = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Restore Blocked Dept");
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, deptId, "Restore Blocked Dept Position", "RESBD");
        var positionId = position.GetProperty("id").GetGuid();

        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{positionId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{deptId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var restore = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{positionId}/restore",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        restore.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Restore_BlockedWhenReportsToPositionInactive_Returns422()
    {
        var manager = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Restore Blocked Manager Position", "RESBM");
        var managerId = manager.GetProperty("id").GetGuid();
        var report = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Restore Blocked Report Position", "RESBR",
            reportsToPositionId: managerId);
        var reportId = report.GetProperty("id").GetGuid();

        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{reportId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var archiveManager = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{managerId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archiveManager.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restore = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{reportId}/restore",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        restore.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // -- Cross-tenant / cross-legal-entity isolation -------------------------
    // 404 is the correct "blocked" semantic here (existence-hiding), matching the
    // same convention already established by DepartmentsIntegrationTests/LegalEntitiesIntegrationTests.

    [Fact]
    public async Task Get_CrossTenant_Returns404()
    {
        var position = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Cross Tenant Position", "XTGET");

        var response = await SendAsync(HttpMethod.Get, _tenantBOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{position.GetProperty("id").GetGuid()}",
            body: null, cookie: _tenantBOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_CrossTenant_LegalEntityId_Returns404()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantBOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            body: null, cookie: _tenantBOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_CrossTenantDepartmentId_Returns404()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions",
            new { departmentId = _tenantBDepartmentId, name = "RLS Bypass Attempt Position", code = "RLSBY", maxOccupancy = 1, reportsToPositionId = (Guid?)null },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Management Coverage ------------------------------------------------

    [Fact]
    public async Task GetCoverage_Unauthenticated_Returns401()
    {
        var owner = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Auth Owner", "COVAU");

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{owner.GetProperty("id").GetGuid()}/coverage",
            body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCoverage_NoRecords_Returns200EmptyArray()
    {
        var owner = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Empty Owner", "COVEM");

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{owner.GetProperty("id").GetGuid()}/coverage",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.ValueKind.Should().Be(JsonValueKind.Array);
        json.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AddCoverage_Primary_Succeeds_AndAppearsInGet()
    {
        var owner = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Manual Owner", "COVMG");
        var ownerId = owner.GetProperty("id").GetGuid();
        var covered = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Manual Target", "COVTG");
        var coveredId = covered.GetProperty("id").GetGuid();

        var addResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerId}/coverage",
            new { coveredTargetType = "Position", coveredPositionId = coveredId, coveredDepartmentId = (Guid?)null, ownerOrder = 1 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var added = await ReadJsonAsync(addResponse);
        added.GetProperty("ownerOrder").GetInt32().Should().Be(1);
        added.GetProperty("source").GetString().Should().Be("Manual");
        added.GetProperty("isLocked").GetBoolean().Should().BeFalse();

        var listJson = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerId}/coverage");
        listJson.GetArrayLength().Should().Be(1);
        listJson[0].GetProperty("coveredPositionId").GetGuid().Should().Be(coveredId);
    }

    [Fact]
    public async Task AddCoverage_DuplicatePrimaryForSameCoveredTarget_Returns409()
    {
        var ownerOne = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Dup Owner One", "CVDP1");
        var ownerTwo = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Dup Owner Two", "CVDP2");
        var covered = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Dup Target", "CVDPT");
        var coveredId = covered.GetProperty("id").GetGuid();

        var firstAdd = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerOne.GetProperty("id").GetGuid()}/coverage",
            new { coveredTargetType = "Position", coveredPositionId = coveredId, coveredDepartmentId = (Guid?)null, ownerOrder = 1 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        firstAdd.StatusCode.Should().Be(HttpStatusCode.OK);

        // A second, different owner position also attempting to become Primary (order 1) for the
        // same covered target must be rejected - uniqueness is per covered target, not per owner.
        var secondAdd = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerTwo.GetProperty("id").GetGuid()}/coverage",
            new { coveredTargetType = "Position", coveredPositionId = coveredId, coveredDepartmentId = (Guid?)null, ownerOrder = 1 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        secondAdd.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddCoverage_BackupOrderBeyondThree_Succeeds()
    {
        var owner = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Deep Owner", "COVDP");
        var covered = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Deep Target", "CVDPG");

        // ownerOrder 4 (Backup Manager 3) must be accepted - responsibility levels are not capped
        // at the historical Primary/Backup 1/Backup 2 trio.
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{owner.GetProperty("id").GetGuid()}/coverage",
            new { coveredTargetType = "Position", coveredPositionId = covered.GetProperty("id").GetGuid(), coveredDepartmentId = (Guid?)null, ownerOrder = 4 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("ownerOrder").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task AddCoverage_InactiveCoveredPosition_Returns422()
    {
        var owner = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Inactive Owner", "COVIO");
        var covered = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Inactive Target", "COVIT");
        var coveredId = covered.GetProperty("id").GetGuid();

        var archiveResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{coveredId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{owner.GetProperty("id").GetGuid()}/coverage",
            new { coveredTargetType = "Position", coveredPositionId = coveredId, coveredDepartmentId = (Guid?)null, ownerOrder = 1 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RemoveCoverage_ManualRecord_Succeeds()
    {
        var owner = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Remove Owner", "COVRO");
        var ownerId = owner.GetProperty("id").GetGuid();
        var covered = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Remove Target", "COVRT");

        var addResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerId}/coverage",
            new { coveredTargetType = "Position", coveredPositionId = covered.GetProperty("id").GetGuid(), coveredDepartmentId = (Guid?)null, ownerOrder = 1 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var added = await ReadJsonAsync(addResponse);
        var coverageId = added.GetProperty("id").GetGuid();

        var removeResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerId}/coverage/{coverageId}/remove",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listJson = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerId}/coverage");
        listJson.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task RemoveCoverage_LockedReportingStructureRecord_Returns409_AndIsNotRemoved()
    {
        var manager = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Locked Manager", "COVLM");
        var managerId = manager.GetProperty("id").GetGuid();
        // Creating a position with reportsToPositionId set is what auto-generates the locked,
        // ReportingStructure-sourced coverage record under test here.
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Locked Report", "COVLR",
            reportsToPositionId: managerId);

        var listJson = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{managerId}/coverage");
        listJson.GetArrayLength().Should().Be(1);
        var lockedRecord = listJson[0];
        lockedRecord.GetProperty("isLocked").GetBoolean().Should().BeTrue();
        lockedRecord.GetProperty("source").GetString().Should().Be("ReportingStructure");
        var coverageId = lockedRecord.GetProperty("id").GetGuid();

        var removeResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{managerId}/coverage/{coverageId}/remove",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var listAfter = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{managerId}/coverage");
        listAfter.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task UpdateCoverage_ChangesOwnerOrder_AndPersists()
    {
        var owner = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Update Owner", "COVUO");
        var ownerId = owner.GetProperty("id").GetGuid();
        var covered = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Update Target", "COVUT");

        var addResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerId}/coverage",
            new { coveredTargetType = "Position", coveredPositionId = covered.GetProperty("id").GetGuid(), coveredDepartmentId = (Guid?)null, ownerOrder = 1 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var added = await ReadJsonAsync(addResponse);
        var coverageId = added.GetProperty("id").GetGuid();

        var updateResponse = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerId}/coverage/{coverageId}",
            new { ownerOrder = 3 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadJsonAsync(updateResponse);
        updated.GetProperty("ownerOrder").GetInt32().Should().Be(3);

        // Re-fetch through GET (a fresh query, not the same tracked instance the PUT handler
        // mutated) to prove the change was actually persisted, not just reflected in the response.
        var listJson = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{ownerId}/coverage");
        listJson.GetArrayLength().Should().Be(1);
        listJson[0].GetProperty("ownerOrder").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task UpdateCoverage_LockedReportingStructureRecord_Returns409_AndIsNotChanged()
    {
        var manager = await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Update Locked Manager", "CVULM");
        var managerId = manager.GetProperty("id").GetGuid();
        await CreatePositionAsync(_tenantAOwner, _tenantALegalEntityId, _tenantADepartmentId, "Coverage Update Locked Report", "CVULR",
            reportsToPositionId: managerId);

        var listJson = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{managerId}/coverage");
        var coverageId = listJson[0].GetProperty("id").GetGuid();

        var updateResponse = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{managerId}/coverage/{coverageId}",
            new { ownerOrder = 5 },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var listAfter = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/positions/{managerId}/coverage");
        listAfter[0].GetProperty("ownerOrder").GetInt32().Should().Be(1);
    }

    // -- Fixture provisioning helpers -----------------------------------------

    private sealed record TenantSession(string Host, string SessionCookie, string CsrfHeader);

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
                name = "Position A Second Co",
                companyCode = "POSA2",
                registrationNumber = "REG-POSA2",
                countryCode = "LKA",
                currencyCode = "LKR"
            },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateDepartmentAsync(TenantSession session, Guid legalEntityId, string name)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host,
            $"/api/v1/org/legal-entities/{legalEntityId}/departments", new { name },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreatePositionAsync(
        TenantSession session, Guid legalEntityId, Guid departmentId, string name, string code,
        int maxOccupancy = 1, Guid? reportsToPositionId = null)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host,
            $"/api/v1/org/legal-entities/{legalEntityId}/positions",
            new { departmentId, name, code, maxOccupancy, reportsToPositionId },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadJsonAsync(response);
    }

    /// <summary>
    /// Sets Department.HeadPositionId directly via DbContext as a test-setup shortcut. Since
    /// Department Part 3, UpdateDepartmentRequest does expose headPositionId as writable through
    /// the real API (see DepartmentsIntegrationTests for that coverage); this helper bypasses it
    /// purely to keep Position-focused archive-blocker scenarios from needing full department
    /// head-assignment setup. Create/UpdatePositionRequest - the Position request contracts -
    /// still never expose headPositionId, by design (see
    /// PositionsControllerArchitectureTests.NoEndpoint_AcceptsOrMutatesHeadPositionId).
    /// </summary>
    private async Task SetDepartmentHeadPositionAsync(Guid departmentId, Guid positionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var department = await db.Departments.SingleAsync(d => d.Id == departmentId);
        department.HeadPositionId = positionId;
        await db.SaveChangesAsync();
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

    // -- HTTP helpers (mirrors DepartmentsIntegrationTests/LegalEntitiesIntegrationTests) --------

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
