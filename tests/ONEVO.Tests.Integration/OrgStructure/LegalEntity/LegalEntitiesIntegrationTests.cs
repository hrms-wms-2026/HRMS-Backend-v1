using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.OrgStructure.LegalEntity;

/// <summary>
/// HTTP integration tests for the Part 2C Legal Entities controller, against a
/// real PostgreSQL database. Two fully-provisioned tenants are created in
/// InitializeAsync (each tenant-creation side effect already seeds one
/// primary legal_entities row - see the Part 1 audit, §1.5) so cross-tenant
/// isolation, duplicate-name-across-tenants, and "last active company" tests
/// have real data to work against without any direct DB seeding of this
/// feature's own rows.
///
/// Database resolution mirrors TenantProvisioningE2ETests: set ONEVO_TEST_DB
/// to run against a local PostgreSQL server with no Docker; otherwise a
/// Testcontainers instance is started.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class LegalEntitiesIntegrationTests : IAsyncLifetime
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

    private const string FixtureUserPassword = "Password123!";

    private TenantSession _tenantA = null!;
    private TenantSession _tenantB = null!;
    private TenantSession _tenantAManager = null!;
    private TenantSession _tenantARegularEmployee = null!;
    private Guid _tenantAId;
    private Guid _tenantAPrimaryLegalEntityId;
    private Guid _tenantBPrimaryLegalEntityId;
    private Guid _tenantASecondLegalEntityId;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_legal_entities_test")
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
        await GrantCoreHrStorageAllowanceAsync();

        var loginResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var adminCookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = adminCookies["admin_csrf"];
        _adminCookie = $"admin_session={adminCookies["admin_session"]}";

        _tenantA = await ProvisionAndLoginOwnerAsync("legal-ent-a", "Legal Ent A Co", "owner-a@legal-ent.test");
        _tenantB = await ProvisionAndLoginOwnerAsync("legal-ent-b", "Legal Ent B Co", "owner-b@legal-ent.test");

        _tenantAPrimaryLegalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantA);
        _tenantBPrimaryLegalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantB);

        _tenantAId = await GetTenantIdAsync(_tenantA.Host);
        var secondCompany = await CreateCompanyAsync(_tenantA, "Fixture Second Co", "FIXSEC1", "REG-FIXSEC1");
        _tenantASecondLegalEntityId = secondCompany.Id;

        _tenantAManager = await SeedAndLoginEmployeeFixtureUserAsync(
            _tenantAId, _tenantA.Host, "manager@legal-ent-a.test",
            permissionCodes: ["org:read", "org:manage", "legal_entity:update", "legal_entity:delete"],
            roleName: "Legal Entity Manager", legalEntityId: _tenantAPrimaryLegalEntityId, employeeNumber: "FIX-MGR-001");

        _tenantARegularEmployee = await SeedAndLoginEmployeeFixtureUserAsync(
            _tenantAId, _tenantA.Host, "regular@legal-ent-a.test",
            permissionCodes: ["org:read", "org:manage"],
            roleName: "Regular Employee", legalEntityId: _tenantASecondLegalEntityId, employeeNumber: "FIX-REG-001");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    // ── 1. Auth required ────────────────────────────────────────────────────

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host, "/api/v1/org/legal-entities", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── 2. List ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsActiveCompanies_ExcludingInactiveByDefault()
    {
        var second = await CreateCompanyAsync(_tenantA, "List Test Extra Co", "LTEC1", "REG-LTEC1");
        var deleteResponse = await DeleteCompanyAsync(_tenantA, second.Id, "List Test Extra Co");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await GetJsonAsync(_tenantA, "/api/v1/org/legal-entities");
        var ids = list.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(_tenantAPrimaryLegalEntityId);
        ids.Should().NotContain(second.Id, "a deactivated company must not appear in the default list view");

        var withInactive = await GetJsonAsync(_tenantA, "/api/v1/org/legal-entities?includeInactive=true");
        withInactive.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(second.Id);
    }

    // ── 3. Get ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetGeneralSettings_OwnCompany_Returns200()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantAPrimaryLegalEntityId}/general-settings",
            body: null, cookie: _tenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetGeneralSettings_OutOfTenantId_Returns404()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantBPrimaryLegalEntityId}/general-settings",
            body: null, cookie: _tenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── 4. Create ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidCompany_Returns201_WithLocationHeader_TenantDerivedFromSession()
    {
        var body = CreateCompanyBody("Newly Created Co", "NEWCO1", "REG-NEWCO1");
        var response = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities", body,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        var id = json.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Contain($"/api/v1/org/legal-entities/{id}/general-settings");

        // Tenant id is server-derived: the new company is invisible to tenant B.
        var crossTenantGet = await SendAsync(HttpMethod.Get, _tenantB.Host,
            $"/api/v1/org/legal-entities/{id}/general-settings", body: null, cookie: _tenantB.SessionCookie);
        crossTenantGet.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_DuplicateNameInsideTenant_Returns409_ButSameNameInAnotherTenantIsAllowed()
    {
        var body = CreateCompanyBody("Duplicate Name Co", "DUPN1", "REG-DUPN1");
        var first = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities", body,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody("Duplicate Name Co", "DUPN2", "REG-DUPN2"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var sameNameOtherTenant = await SendAsync(HttpMethod.Post, _tenantB.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody("Duplicate Name Co", "DUPN3", "REG-DUPN3"),
            cookie: _tenantB.SessionCookie, csrfToken: _tenantB.CsrfHeader);
        sameNameOtherTenant.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_DuplicateCompanyCodeInsideTenant_Returns409()
    {
        var first = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody("Code Dup Co A", "CODEDUP", "REG-CODEDUPA"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody("Code Dup Co B", "CODEDUP", "REG-CODEDUPB"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_DuplicateRegistrationNumberInsideTenant_Returns409()
    {
        var first = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody("Reg Dup Co A", "REGDUPA", "REG-REGDUP"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody("Reg Dup Co B", "REGDUPB", "REG-REGDUP"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_InvalidCountryOrCurrency_Returns400()
    {
        var missingCountry = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            new
            {
                name = "Invalid Country Co",
                companyCode = "INVCC",
                registrationNumber = "REG-INVCC",
                countryCode = "",
                currencyCode = "LKR"
            },
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        missingCountry.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missingCurrency = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            new
            {
                name = "Invalid Currency Co",
                companyCode = "INVCU",
                registrationNumber = "REG-INVCU",
                countryCode = "LKA",
                currencyCode = ""
            },
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        missingCurrency.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── 5. Update ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ValidRequest_Returns200_AndSortsStandardWorkingDays()
    {
        var company = await CreateCompanyAsync(_tenantA, "Update Target Co", "UPDT1", "REG-UPDT1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Update Target Co Updated", "UPDT1", "REG-UPDT1", [5, 1, 3]),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("name").GetString().Should().Be("Update Target Co Updated");
        json.GetProperty("standardWorkingDays").EnumerateArray().Select(e => e.GetInt32())
            .Should().Equal(1, 3, 5);
    }

    [Fact]
    public async Task Update_PreservesIsPrimary_NotExposedInRequest()
    {
        var before = await GetJsonAsync(_tenantA, "/api/v1/org/legal-entities");
        var primaryBefore = before.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == _tenantAPrimaryLegalEntityId);
        primaryBefore.GetProperty("isPrimary").GetBoolean().Should().BeTrue();

        var current = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantAPrimaryLegalEntityId}/general-settings",
            body: null, cookie: _tenantA.SessionCookie);
        var currentJson = await ReadJsonAsync(current);

        await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantAPrimaryLegalEntityId}/general-settings",
            UpdateBody(
                currentJson.GetProperty("name").GetString()!,
                currentJson.GetProperty("companyCode").GetString() ?? "PRIMCODE",
                currentJson.GetProperty("registrationNumber").GetString() ?? "REG-PRIM",
                [1, 2, 3, 4, 5]),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        var after = await GetJsonAsync(_tenantA, "/api/v1/org/legal-entities");
        var primaryAfter = after.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == _tenantAPrimaryLegalEntityId);
        primaryAfter.GetProperty("isPrimary").GetBoolean().Should().BeTrue("isPrimary is not part of the update request and must be preserved");
    }

    [Fact]
    public async Task Update_InvalidStandardWorkingDays_Returns400()
    {
        var company = await CreateCompanyAsync(_tenantA, "Working Days Co", "WDC1", "REG-WDC1");

        var empty = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Working Days Co", "WDC1", "REG-WDC1", []),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var outOfRange = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Working Days Co", "WDC1", "REG-WDC1", [0, 8]),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        outOfRange.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var duplicates = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Working Days Co", "WDC1", "REG-WDC1", [1, 1, 2]),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        duplicates.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_OutOfTenant_Returns404()
    {
        var response = await SendAsync(HttpMethod.Put, _tenantB.Host,
            $"/api/v1/org/legal-entities/{_tenantAPrimaryLegalEntityId}/general-settings",
            UpdateBody("Should Not Apply", "XXX", "REG-XXX", [1, 2, 3, 4, 5]),
            cookie: _tenantB.SessionCookie, csrfToken: _tenantB.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetGeneralSettings_ReturnsWorkStartAndEndTime_AsHhMmStrings()
    {
        var company = await CreateCompanyAsync(_tenantA, "Work Time Get Co", "WTGET1", "REG-WTGET1");
        await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Work Time Get Co", "WTGET1", "REG-WTGET1", [1, 2, 3, 4, 5], "09:00", "17:30"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        var response = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            body: null, cookie: _tenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("workStartTime").GetString().Should().Be("09:00");
        json.GetProperty("workEndTime").GetString().Should().Be("17:30");
    }

    [Fact]
    public async Task Update_ValidWorkStartAndEndTime_Returns200_AndPersists()
    {
        var company = await CreateCompanyAsync(_tenantA, "Work Time Put Co", "WTPUT1", "REG-WTPUT1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Work Time Put Co", "WTPUT1", "REG-WTPUT1", [1, 2, 3, 4, 5], "09:00", "17:30"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("workStartTime").GetString().Should().Be("09:00");
        json.GetProperty("workEndTime").GetString().Should().Be("17:30");
    }

    [Fact]
    public async Task Update_OnlyWorkStartTimeProvided_Returns400()
    {
        var company = await CreateCompanyAsync(_tenantA, "Work Time Only Start Co", "WTOS1", "REG-WTOS1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Work Time Only Start Co", "WTOS1", "REG-WTOS1", [1, 2, 3, 4, 5], "09:00", null),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_OnlyWorkEndTimeProvided_Returns400()
    {
        var company = await CreateCompanyAsync(_tenantA, "Work Time Only End Co", "WTOE1", "REG-WTOE1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Work Time Only End Co", "WTOE1", "REG-WTOE1", [1, 2, 3, 4, 5], null, "17:30"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_WorkStartTimeNotBeforeEndTime_Returns400()
    {
        var company = await CreateCompanyAsync(_tenantA, "Work Time Bad Order Co", "WTBO1", "REG-WTBO1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Work Time Bad Order Co", "WTBO1", "REG-WTBO1", [1, 2, 3, 4, 5], "18:00", "09:00"),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetGeneralSettings_ReturnsBreakDurationMinutes()
    {
        var company = await CreateCompanyAsync(_tenantA, "Break Duration Get Co", "BDGET1", "REG-BDGET1");
        await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Break Duration Get Co", "BDGET1", "REG-BDGET1", [1, 2, 3, 4, 5], breakDurationMinutes: 60),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        var response = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            body: null, cookie: _tenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("breakDurationMinutes").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task Update_ValidBreakDurationMinutes_Returns200_AndPersists()
    {
        var company = await CreateCompanyAsync(_tenantA, "Break Duration Put Co", "BDPUT1", "REG-BDPUT1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Break Duration Put Co", "BDPUT1", "REG-BDPUT1", [1, 2, 3, 4, 5], breakDurationMinutes: 45),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("breakDurationMinutes").GetInt32().Should().Be(45);
    }

    [Fact]
    public async Task Update_NullBreakDurationMinutes_Returns200_AndPersistsNull()
    {
        var company = await CreateCompanyAsync(_tenantA, "Break Duration Null Co", "BDNULL1", "REG-BDNULL1");
        await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Break Duration Null Co", "BDNULL1", "REG-BDNULL1", [1, 2, 3, 4, 5], breakDurationMinutes: 30),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Break Duration Null Co", "BDNULL1", "REG-BDNULL1", [1, 2, 3, 4, 5], breakDurationMinutes: null),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("breakDurationMinutes").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Update_NegativeBreakDurationMinutes_Returns400()
    {
        var company = await CreateCompanyAsync(_tenantA, "Break Duration Negative Co", "BDNEG1", "REG-BDNEG1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Break Duration Negative Co", "BDNEG1", "REG-BDNEG1", [1, 2, 3, 4, 5], breakDurationMinutes: -1),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_DecimalBreakDurationMinutes_Returns400()
    {
        // BreakDurationMinutes binds as int? - a decimal value fails JSON model binding
        // itself (before FluentValidation ever runs), and [ApiController] turns that into
        // an automatic 400. Sent as a raw anonymous object (not the typed UpdateBody
        // helper, which only accepts int?) so the wire payload can carry a decimal.
        var company = await CreateCompanyAsync(_tenantA, "Break Duration Decimal Co", "BDDEC1", "REG-BDDEC1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            new
            {
                name = "Break Duration Decimal Co",
                companyCode = "BDDEC1",
                registrationNumber = "REG-BDDEC1",
                countryCode = "LKA",
                currencyCode = "LKR",
                timezone = "Asia/Colombo",
                financialYearStartMonth = 1,
                firstDayOfWeek = 1,
                standardWorkingDays = new[] { 1, 2, 3, 4, 5 },
                defaultLanguage = "en-US",
                dateFormat = "DD MMM YYYY",
                timeFormat = "12h",
                status = "active",
                workStartTime = (string?)null,
                workEndTime = (string?)null,
                breakDurationMinutes = 7.5
            },
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_BreakDurationMinutes_IndependentOfWorkStartAndEndTime()
    {
        var company = await CreateCompanyAsync(_tenantA, "Break Duration Independent Co", "BDIND1", "REG-BDIND1");

        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings",
            UpdateBody("Break Duration Independent Co", "BDIND1", "REG-BDIND1", [1, 2, 3, 4, 5],
                workStartTime: null, workEndTime: null, breakDurationMinutes: 15),
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("breakDurationMinutes").GetInt32().Should().Be(15);
        json.GetProperty("workStartTime").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("workEndTime").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── 6. Delete ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExactConfirmName_Returns204_AndSoftDeactivatesOnly()
    {
        var company = await CreateCompanyAsync(_tenantA, "Delete Target Co", "DELT1", "REG-DELT1");

        var response = await DeleteCompanyAsync(_tenantA, company.Id, "Delete Target Co");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Row still exists (GET still resolves it), only its status flips.
        var afterDelete = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings", body: null, cookie: _tenantA.SessionCookie);
        afterDelete.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(afterDelete);
        json.GetProperty("status").GetString().Should().Be("inactive");
    }

    [Fact]
    public async Task Delete_ConfirmNameMismatch_Returns400()
    {
        var company = await CreateCompanyAsync(_tenantA, "Mismatch Target Co", "MISM1", "REG-MISM1");

        var response = await DeleteCompanyAsync(_tenantA, company.Id, "Wrong Name");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_LastActiveCompanyInTenant_Returns400()
    {
        // Tenant B was provisioned with exactly one (primary) legal entity and
        // this test suite has not created extra companies against it, so its
        // primary company is still the last active one.
        var response = await DeleteCompanyAsync(_tenantB, _tenantBPrimaryLegalEntityId, "Legal Ent B Co");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("last active company");
    }

    [Fact]
    public async Task Delete_OutOfTenant_Returns404()
    {
        var response = await SendAsync(HttpMethod.Delete, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantBPrimaryLegalEntityId}",
            new { confirmName = "Legal Ent B Co" },
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── 7. Logo ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveLogo_ClearsLogoFileId_ReturnsNoContent()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo Remove Co", "LOGOC1", "REG-LOGOC1");

        var response = await SendAsync(HttpMethod.Delete, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", body: null,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/general-settings", body: null, cookie: _tenantA.SessionCookie);
        var json = await ReadJsonAsync(after);
        json.GetProperty("logoFileId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SetLogo_ValidImage_UploadsAndReturnsFileId()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo Upload Co", "LOGOU1", "REG-LOGOU1");

        using var content = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG magic bytes + padding
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "logo", "logo.png");

        var response = await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", content,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        json.GetProperty("logoFileId").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task SetLogo_OversizedFile_IsRejected()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo Oversize Co", "LOGOU2", "REG-LOGOU2");

        using var content = new MultipartFormDataContent();
        var bytes = new byte[6 * 1024 * 1024]; // over the 5 MB company_logo purpose limit
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "logo", "big.png");

        var response = await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", content,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetLogo_WrongContentType_IsRejected()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo WrongType Co", "LOGOU3", "REG-LOGOU3");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("not an image"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "logo", "notes.txt");

        var response = await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", content,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLogo_NoLogoSet_Returns404()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo NoneSet Co", "LOGOU4", "REG-LOGOU4");

        var response = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", body: null, cookie: _tenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLogo_AfterUpload_ReturnsImageBytes()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo RoundTrip Co", "LOGOU5", "REG-LOGOU5");

        using var uploadContent = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        uploadContent.Add(fileContent, "logo", "logo.png");
        var uploadResponse = await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", uploadContent,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK, await uploadResponse.Content.ReadAsStringAsync());

        var getResponse = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", body: null, cookie: _tenantA.SessionCookie);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var returnedBytes = await getResponse.Content.ReadAsByteArrayAsync();
        returnedBytes.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task RemoveLogo_ThenGetLogo_Returns404Again()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo RemoveThenGet Co", "LOGOU6", "REG-LOGOU6");

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        uploadContent.Add(fileContent, "logo", "logo.png");
        await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", uploadContent,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        await SendAsync(HttpMethod.Delete, _tenantA.Host, $"/api/v1/org/legal-entities/{company.Id}/logo",
            body: null, cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        var getResponse = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", body: null, cookie: _tenantA.SessionCookie);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── 8. Accessible-company filtering ─────────────────────────────────────

    [Fact]
    public async Task List_Owner_SeesAllActiveCompaniesInTenant()
    {
        var list = await GetJsonAsync(_tenantA, "/api/v1/org/legal-entities");
        var ids = list.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(_tenantAPrimaryLegalEntityId);
        ids.Should().Contain(_tenantASecondLegalEntityId);
    }

    [Fact]
    public async Task List_ManagerWithLegalEntityUpdate_SeesAllActiveCompaniesInTenant()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host, "/api/v1/org/legal-entities",
            body: null, cookie: _tenantAManager.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var ids = json.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(_tenantAPrimaryLegalEntityId);
        ids.Should().Contain(_tenantASecondLegalEntityId);
    }

    [Fact]
    public async Task List_RegularEmployee_SeesOnlyOwnLegalEntity()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host, "/api/v1/org/legal-entities",
            body: null, cookie: _tenantARegularEmployee.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var ids = json.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().ContainSingle().Which.Should().Be(_tenantASecondLegalEntityId);
    }

    [Fact]
    public async Task List_RegularEmployee_IncludeInactiveTrue_StillOnlyOwnActiveCompany()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host, "/api/v1/org/legal-entities?includeInactive=true",
            body: null, cookie: _tenantARegularEmployee.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var ids = json.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().ContainSingle().Which.Should().Be(_tenantASecondLegalEntityId);
    }

    [Fact]
    public async Task RegularEmployee_CannotUpdateOrDeleteLegalEntity_WithoutPermission()
    {
        var updateResponse = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/general-settings",
            UpdateBody("Should Not Apply", "FIXSEC1", "REG-FIXSEC1", [1, 2, 3, 4, 5]),
            cookie: _tenantARegularEmployee.SessionCookie, csrfToken: _tenantARegularEmployee.CsrfHeader);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteResponse = await SendAsync(HttpMethod.Delete, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}",
            new { confirmName = "Fixture Second Co" },
            cookie: _tenantARegularEmployee.SessionCookie, csrfToken: _tenantARegularEmployee.CsrfHeader);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetGeneralSettings_RegularEmployeeAnotherCompany_Returns403_ViaPermissionGate()
    {
        // The RequirePermission("legal_entity:update") attribute on this route rejects a
        // caller without that permission before GetAccessibleByIdAsync's own
        // accessible-company filter is ever reached - under the current flat RBAC model,
        // holding legal_entity:update already implies management access
        // (LegalEntityAccessPolicy.HasManagementAccess), so no HTTP-reachable caller can
        // exercise the filter's non-management branch. This test documents that the
        // required "regular user cannot GET another company" behavior still holds today;
        // GetLegalEntityGeneralSettingsQueryHandlerTests covers the filter itself in
        // isolation. See LEGAL_ENTITY_CREATE_ACCESS_REPORT.md.
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantAPrimaryLegalEntityId}/general-settings",
            body: null, cookie: _tenantARegularEmployee.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateGeneralSettings_RegularEmployeeAnotherCompany_Returns403_ViaPermissionGate()
    {
        var response = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantAPrimaryLegalEntityId}/general-settings",
            UpdateBody("Should Not Apply", "XXX", "REG-XXX", [1, 2, 3, 4, 5]),
            cookie: _tenantARegularEmployee.SessionCookie, csrfToken: _tenantARegularEmployee.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetGeneralSettings_ManagerWithLegalEntityUpdate_AnotherCompanyInTenant_Returns200()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/general-settings",
            body: null, cookie: _tenantAManager.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ManagerWithLegalEntityUpdate_CanUpdate_ButCreateStillRequiresLegalEntityCreate()
    {
        // _tenantAManager was seeded with legal_entity:update and legal_entity:delete but
        // deliberately not legal_entity:create, so this also proves Create is gated
        // independently from Update/Delete.
        var createResponse = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody("Manager Create Attempt Co", "MGRCA1", "REG-MGRCA1"),
            cookie: _tenantAManager.SessionCookie, csrfToken: _tenantAManager.CsrfHeader);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updateResponse = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/general-settings",
            UpdateBody("Fixture Second Co Renamed", "FIXSEC1", "REG-FIXSEC1", [1, 2, 3, 4, 5]),
            cookie: _tenantAManager.SessionCookie, csrfToken: _tenantAManager.CsrfHeader);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Provisioning helper (trimmed from TenantProvisioningE2ETests) ───────────

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

        const string baseHost = "localhost";
        var loginResponse = await SendAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email = ownerEmail, password = ownerPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var loginJson = await ReadJsonAsync(loginResponse);
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange",
            new { code = exchangeCode });
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);

        return new TenantSession(host, sessionCookie, csrfHeader);
    }

    private async Task<Guid> GetPrimaryLegalEntityIdAsync(TenantSession session)
    {
        var list = await GetJsonAsync(session, "/api/v1/org/legal-entities");
        var primary = list.EnumerateArray().Single(i => i.GetProperty("isPrimary").GetBoolean());
        return primary.GetProperty("id").GetGuid();
    }

    private async Task<Guid> GetTenantIdAsync(string host)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var slug = host.Split('.')[0];
        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Slug == slug);
        return tenant.Id;
    }

    private async Task<TenantSession> SeedAndLoginEmployeeFixtureUserAsync(
        Guid tenantId, string host, string email, IReadOnlyList<string> permissionCodes,
        string roleName, Guid legalEntityId, string employeeNumber)
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
            Description = $"Legal entity fixture role: {roleName}",
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

        db.Add(new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            EmployeeNumber = employeeNumber,
            FirstName = "Fixture",
            LastName = roleName,
            Email = email,
            LegalEntityId = legalEntityId,
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1),
            CreatedAt = now,
            CreatedById = userId
        });

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

        const string baseHost = "localhost";
        var loginResponse = await SendAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email, password = FixtureUserPassword });
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

    private async Task<(Guid Id, JsonElement Body)> CreateCompanyAsync(
        TenantSession session, string name, string companyCode, string registrationNumber)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody(name, companyCode, registrationNumber),
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        return (json.GetProperty("id").GetGuid(), json);
    }

    private async Task<HttpResponseMessage> DeleteCompanyAsync(TenantSession session, Guid id, string confirmName)
        => await SendAsync(HttpMethod.Delete, session.Host, $"/api/v1/org/legal-entities/{id}",
            new { confirmName }, cookie: session.SessionCookie, csrfToken: session.CsrfHeader);

    private static object CreateCompanyBody(string name, string companyCode, string registrationNumber) => new
    {
        name,
        companyCode,
        registrationNumber,
        countryCode = "LKA",
        currencyCode = "LKR"
    };

    private static object UpdateBody(
        string name, string companyCode, string registrationNumber, IEnumerable<int> workingDays,
        string? workStartTime = null, string? workEndTime = null, int? breakDurationMinutes = null) => new
    {
        name,
        companyCode,
        registrationNumber,
        countryCode = "LKA",
        currencyCode = "LKR",
        timezone = "Asia/Colombo",
        financialYearStartMonth = 1,
        firstDayOfWeek = 1,
        standardWorkingDays = workingDays,
        defaultLanguage = "en-US",
        dateFormat = "DD MMM YYYY",
        timeFormat = "12h",
        status = "active",
        workStartTime,
        workEndTime,
        breakDurationMinutes
    };

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

    /// <summary>
    /// No module_catalog row carries a real storage_reference in the production
    /// seed data yet (ModuleCatalogSeeder leaves every module at "[]"), and no
    /// platform default is configured - so any tenant is "storage_not_entitled"
    /// by default. This mirrors StorageQuotaIntegrationTests' own workaround:
    /// grant the "core_hr" module (present on every tenant's subscription here)
    /// a real allowance for the "11-50" company size range used when
    /// provisioning tenants in this test file, purely so the logo upload tests
    /// below can exercise the real quota path. Test-only; no production code
    /// or seed data is touched.
    /// </summary>
    private async Task GrantCoreHrStorageAllowanceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coreHr = await db.Set<ModuleCatalogItem>().SingleAsync(m => m.ModuleKey == "core_hr");
        coreHr.IsStorageConsuming = true;
        coreHr.StorageReference = """[{"min_employees":1,"max_employees":100,"storage_gb":50}]""";
        await db.SaveChangesAsync();
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

    // ── HTTP helpers (mirrors TenantProvisioningE2ETests) ───────────────────────

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

    private async Task<HttpResponseMessage> SendMultipartAsync(
        HttpMethod method, string host, string path, HttpContent content,
        string? cookie = null, string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-Token", csrfToken);

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
