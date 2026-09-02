using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using WorkAreaSources = ONEVO.Application.Features.TimeAttendance.Services.ExpectedWorkAreaResolver;
using TodaySnapshotSource = ONEVO.Application.Features.TimeAttendance.Services.AttendanceTodayStateService;

namespace ONEVO.Tests.Integration.Features.TimeAttendance;

/// <summary>
/// Real HTTP/PostgreSQL end-to-end coverage for the Work Area Change Request runtime path:
/// submit request -> approve -> read Today -> clock in -> read history. Reuses the proven
/// WebApplicationFactory/tenant-provisioning pattern from AttendanceCorrectionsIntegrationTests
/// rather than inventing a new fixture style. WorkDate is resolved from the real clock at fixture
/// setup time, and the legal entity's working-day set is configured to include every day of the
/// week, so schedule/working-day resolution never depends on which real weekday the suite runs on.
///
/// Each [Fact] gets its own fresh Testcontainers database and tenant provisioning (xUnit
/// constructs a new class instance per test method, so IAsyncLifetime.InitializeAsync reruns per
/// test) - the same isolation-over-shared-state tradeoff AttendanceCorrectionsIntegrationTests
/// already makes in this suite.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WorkAreaChangeRequestRuntimeHttpIntegrationTests : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private const string FixtureUserPassword = "Password123!";
    private const string ApprovePermission = "attendance:approve";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");
    private static readonly TimeZoneInfo ColomboZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    // Resolved from the real clock in InitializeAsync (not hardcoded) so the fixture never fights
    // ASP.NET Core's own real-time cookie/ticket expiry checks. The legal entity's working-day set
    // is configured to include every day of the week (see ConfigureLegalEntityGeneralSettingsAsync)
    // so schedule resolution does not depend on which real weekday the suite happens to run on.
    private DateOnly WorkDate;

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private string _connectionString = null!;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    private Guid _tenantAId;
    private Guid _legalEntityAId;
    private Guid _ownerAUserId;
    private Guid _approverPositionId;

    private TenantSession _requesterA = null!;
    private Guid _requesterAEmployeeId;
    private Guid _requesterAUserId;

    private TenantSession _requesterA2 = null!;
    private Guid _requesterA2EmployeeId;

    private TenantSession _requesterA3 = null!;
    private Guid _requesterA3EmployeeId;

    private TenantSession _approverA = null!;
    private Guid _approverAEmployeeId;
    private Guid _approverAUserId;

    private TenantSession _wrongApproverA = null!;

    private Guid _tenantBId;
    private TenantSession _approverB = null!;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_work_area_runtime_http_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
        }

        await AdminTestFactory.MigrateDatabaseAsync(_connectionString);

        WorkDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ColomboZone).DateTime);

        _environmentScope = new IntegrationTestEnvironmentScope(_connectionString);
        _factory = new E2ETestFactory(_connectionString, _email);
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

        var ownerA = await ProvisionAndLoginOwnerAsync("wa-run-a", "Work Area Runtime A Co", "owner-a@wa-run.test");
        var ownerB = await ProvisionAndLoginOwnerAsync("wa-run-b", "Work Area Runtime B Co", "owner-b@wa-run.test");

        _tenantAId = await GetTenantIdAsync(ownerA.Host);
        _tenantBId = await GetTenantIdAsync(ownerB.Host);
        _legalEntityAId = await GetPrimaryLegalEntityIdAsync(ownerA);
        var legalEntityBId = await GetPrimaryLegalEntityIdAsync(ownerB);
        _ownerAUserId = await GetUserIdByEmailAsync(_tenantAId, "owner-a@wa-run.test");
        var ownerBUserId = await GetUserIdByEmailAsync(_tenantBId, "owner-b@wa-run.test");

        await ConfigureLegalEntityGeneralSettingsAsync(_legalEntityAId);
        await ConfigureLegalEntityGeneralSettingsAsync(legalEntityBId);
        await SeedClockInPolicyAsync(_tenantAId, _legalEntityAId, _ownerAUserId);
        await SeedClockInPolicyAsync(_tenantBId, legalEntityBId, ownerBUserId);

        _approverPositionId = await SeedPositionAsync(_tenantAId, _legalEntityAId, "Approver Position", null);
        var requesterPositionId = await SeedPositionAsync(_tenantAId, _legalEntityAId, "Requester Position", _approverPositionId);
        var requesterPosition2Id = await SeedPositionAsync(_tenantAId, _legalEntityAId, "Requester Position 2", _approverPositionId);
        var requesterPosition3Id = await SeedPositionAsync(_tenantAId, _legalEntityAId, "Requester Position 3", _approverPositionId);

        (_approverA, _approverAEmployeeId, _approverAUserId) = await SeedEmployeeFixtureUserAsync(
            _tenantAId, ownerA.Host, "approver@wa-run-a.test", _legalEntityAId, "WA-A-APR-001", workModeId: 1);
        (_requesterA, _requesterAEmployeeId, _requesterAUserId) = await SeedEmployeeFixtureUserAsync(
            _tenantAId, ownerA.Host, "requester@wa-run-a.test", _legalEntityAId, "WA-A-REQ-001", workModeId: 1);
        (_requesterA2, _requesterA2EmployeeId, _) = await SeedEmployeeFixtureUserAsync(
            _tenantAId, ownerA.Host, "requester2@wa-run-a.test", _legalEntityAId, "WA-A-REQ-002", workModeId: 1);
        (_requesterA3, _requesterA3EmployeeId, _) = await SeedEmployeeFixtureUserAsync(
            _tenantAId, ownerA.Host, "requester3@wa-run-a.test", _legalEntityAId, "WA-A-REQ-003", workModeId: 1);
        (_wrongApproverA, _, var wrongApproverAUserId) = await SeedEmployeeFixtureUserAsync(
            _tenantAId, ownerA.Host, "wrong-approver@wa-run-a.test", _legalEntityAId, "WA-A-WRG-001", workModeId: 1);
        (_approverB, _, var approverBUserId) = await SeedEmployeeFixtureUserAsync(
            _tenantBId, ownerB.Host, "approver@wa-run-b.test", legalEntityBId, "WA-B-APR-001", workModeId: 1);

        await AssignPrimaryPositionAsync(_tenantAId, _approverAEmployeeId, _approverPositionId, _ownerAUserId, null);
        await AssignPrimaryPositionAsync(_tenantAId, _requesterAEmployeeId, requesterPositionId, _ownerAUserId, _approverAEmployeeId);
        await AssignPrimaryPositionAsync(_tenantAId, _requesterA2EmployeeId, requesterPosition2Id, _ownerAUserId, _approverAEmployeeId);
        await AssignPrimaryPositionAsync(_tenantAId, _requesterA3EmployeeId, requesterPosition3Id, _ownerAUserId, _approverAEmployeeId);

        await GrantPermissionAsync(_tenantAId, _approverAUserId, ApprovePermission);
        await GrantPermissionAsync(_tenantAId, wrongApproverAUserId, ApprovePermission);
        await GrantPermissionAsync(_tenantBId, approverBUserId, ApprovePermission);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    // ── Primary end-to-end scenario ─────────────────────────────────────────

    [Fact]
    public async Task FullLifecycle_SubmitApproveClockInHistory_ReflectsApprovedRemoteOverride()
    {
        // Step 0 (negative, folded in): an unsupported requested work area is rejected by the
        // request-level FluentValidation rule before any valid request is created, and does not
        // consume the one-active-request-per-day slot.
        var unsupportedResponse = await SendAsync(HttpMethod.Post, _requesterA.Host,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "field", reason = "Unsupported" },
            cookie: _requesterA.SessionCookie, csrfToken: _requesterA.CsrfHeader);
        unsupportedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Step 1: baseline Today is the permanent On-site work mode.
        var today1 = await GetJsonAuthenticatedAsync(_requesterA, "/api/v1/attendance/time-tracking/today");
        today1.GetProperty("expectedWorkMode").GetString().Should().Be("onsite");
        today1.GetProperty("expectedWorkAreaSource").GetString().Should().Be(WorkAreaSources.SourceActiveWorkMode);
        AssertAllowedMethods(today1, web: true, tray: false, photo: false);

        // Step 2: preview.
        var preview = await PostJsonAuthenticatedAsync(_requesterA,
            "/api/v1/attendance/work-area-change-requests/preview",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "Home repair appointment" });
        preview.status.Should().Be(HttpStatusCode.OK, preview.json.ValueKind == JsonValueKind.Undefined ? "(empty body)" : preview.json.GetRawText());
        preview.json.GetProperty("currentExpectedWorkArea").GetString().Should().Be("onsite");
        preview.json.GetProperty("requestedWorkArea").GetString().Should().Be("remote");
        preview.json.GetProperty("receiver").GetProperty("userId").GetGuid().Should().Be(_approverAUserId);

        // Step 3: submit.
        var create = await PostJsonAuthenticatedAsync(_requesterA,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "Home repair appointment" });
        create.status.Should().Be(HttpStatusCode.Created);
        create.json.GetProperty("status").GetString().Should().Be(WorkAreaChangeRequest.StatusPending);
        create.json.GetProperty("requestedWorkArea").GetString().Should().Be("remote");
        create.json.TryGetProperty("tenantId", out _).Should().BeFalse("tenant id is server-internal and must not be exposed");
        var requestId = create.json.GetProperty("id").GetGuid();

        // A second active request for the same employee/date is rejected while the first is pending.
        var duplicate = await SendAsync(HttpMethod.Post, _requesterA.Host,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "onsite", reason = "Duplicate attempt" },
            cookie: _requesterA.SessionCookie, csrfToken: _requesterA.CsrfHeader);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Pending does not affect Today.
        var todayWhilePending = await GetJsonAuthenticatedAsync(_requesterA, "/api/v1/attendance/time-tracking/today");
        todayWhilePending.GetProperty("expectedWorkMode").GetString().Should().Be("onsite");

        // Step 4: approval inbox.
        var inboxAsApprover = await GetJsonAuthenticatedAsync(_approverA, "/api/v1/attendance/work-area-change-requests/approvals");
        var inboxItems = inboxAsApprover.GetProperty("items").EnumerateArray().ToList();
        inboxItems.Should().ContainSingle(x => x.GetProperty("id").GetGuid() == requestId);

        var inboxAsWrongApprover = await GetJsonAuthenticatedAsync(_wrongApproverA, "/api/v1/attendance/work-area-change-requests/approvals");
        inboxAsWrongApprover.GetProperty("items").EnumerateArray()
            .Should().NotContain(x => x.GetProperty("id").GetGuid() == requestId,
                "wrongApprover holds attendance:approve but is not the resolver-selected approver for this employee");

        var inboxAsRequester = await SendAsync(HttpMethod.Get, _requesterA.Host,
            "/api/v1/attendance/work-area-change-requests/approvals", body: null, cookie: _requesterA.SessionCookie);
        inboxAsRequester.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the requester does not hold attendance:approve");

        // Wrong approver (has the permission, is not the selected route) cannot approve.
        var wrongApprove = await PostJsonAuthenticatedAsync(_wrongApproverA,
            $"/api/v1/attendance/work-area-change-requests/{requestId}/approve", new { reviewComment = (string?)null });
        wrongApprove.status.Should().Be(HttpStatusCode.Forbidden);

        // Step 5: approve.
        var approve = await PostJsonAuthenticatedAsync(_approverA,
            $"/api/v1/attendance/work-area-change-requests/{requestId}/approve", new { reviewComment = (string?)null });
        approve.status.Should().Be(HttpStatusCode.OK);
        approve.json.GetProperty("status").GetString().Should().Be(WorkAreaChangeRequest.StatusApproved);
        approve.json.GetProperty("reviewedById").GetGuid().Should().Be(_approverAUserId);
        approve.json.GetProperty("requestedWorkArea").GetString().Should().Be("remote");

        // Approving an already-decided request returns the existing conflict behavior.
        var reapprove = await PostJsonAuthenticatedAsync(_approverA,
            $"/api/v1/attendance/work-area-change-requests/{requestId}/approve", new { reviewComment = (string?)null });
        reapprove.status.Should().Be(HttpStatusCode.Conflict);

        // Step 6: Today uses the approved override before any attendance row exists.
        var today2 = await GetJsonAuthenticatedAsync(_requesterA, "/api/v1/attendance/time-tracking/today");
        today2.GetProperty("expectedWorkMode").GetString().Should().Be("remote");
        today2.GetProperty("expectedWorkAreaSource").GetString().Should().Be(WorkAreaSources.SourceApprovedRequest);
        AssertAllowedMethods(today2, web: true, tray: true, photo: true);

        // Step 7: clock in.
        var clockIn = await PostJsonAuthenticatedAsync(_requesterA,
            "/api/v1/attendance/time-tracking/clock-in", new { source = "web" });
        clockIn.status.Should().Be(HttpStatusCode.OK);
        clockIn.json.GetProperty("expectedWorkMode").GetString().Should().Be("remote");
        clockIn.json.GetProperty("attendanceSource").GetString().Should().Be("web");
        clockIn.json.GetProperty("clockInAt").GetDateTimeOffset().Should().NotBe(default);
        clockIn.json.GetProperty("expectedWorkAreaSource").GetString().Should().Be(TodaySnapshotSource.ExpectedWorkAreaSourceAttendanceSnapshot);

        await using (var verifyDb = OpenScopedDb())
        {
            var record = await verifyDb.AttendanceRecords.AsNoTracking()
                .SingleAsync(x => x.TenantId == _tenantAId && x.EmployeeId == _requesterAEmployeeId && x.Date == WorkDate);
            record.ExpectedWorkArea.Should().Be("remote");
        }

        // Step 8: Today after clock-in still reflects the persisted snapshot.
        var today3 = await GetJsonAuthenticatedAsync(_requesterA, "/api/v1/attendance/time-tracking/today");
        today3.GetProperty("expectedWorkMode").GetString().Should().Be("remote");
        today3.GetProperty("expectedWorkAreaSource").GetString().Should().Be(TodaySnapshotSource.ExpectedWorkAreaSourceAttendanceSnapshot);

        // Step 9: history.
        var history = await GetJsonAuthenticatedAsync(_requesterA,
            $"/api/v1/attendance/time-tracking/history?from={WorkDate:yyyy-MM-dd}&to={WorkDate:yyyy-MM-dd}");
        var historyRow = history.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("workDate").GetString() == WorkDate.ToString("yyyy-MM-dd"));
        historyRow.GetProperty("expectedWorkMode").GetString().Should().Be("remote");

        // Step 10: database invariants.
        await using var db = OpenScopedDb();
        (await db.WorkAreaChangeRequests.AsNoTracking()
            .CountAsync(x => x.TenantId == _tenantAId && x.EmployeeId == _requesterAEmployeeId
                && x.Date == WorkDate && x.Status == WorkAreaChangeRequest.StatusApproved))
            .Should().Be(1);
        (await db.AttendanceRecords.AsNoTracking()
            .CountAsync(x => x.TenantId == _tenantAId && x.EmployeeId == _requesterAEmployeeId && x.Date == WorkDate))
            .Should().Be(1);
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == _requesterAEmployeeId);
        employee.WorkModeId.Should().Be(1, "approval must never mutate the employee's permanent WorkModeId");
    }

    [Fact]
    public async Task ApprovalAfterClockIn_SynchronizesExistingAttendanceSnapshot()
    {
        var clockIn = await PostJsonAuthenticatedAsync(_requesterA2,
            "/api/v1/attendance/time-tracking/clock-in", new { source = "web" });
        clockIn.status.Should().Be(HttpStatusCode.OK);
        clockIn.json.GetProperty("expectedWorkMode").GetString().Should().Be("onsite");

        Guid attendanceRecordId;
        DateTimeOffset actualStart;
        await using (var db = OpenScopedDb())
        {
            var record = await db.AttendanceRecords.AsNoTracking()
                .SingleAsync(x => x.TenantId == _tenantAId && x.EmployeeId == _requesterA2EmployeeId && x.Date == WorkDate);
            attendanceRecordId = record.Id;
            actualStart = record.ActualStart!.Value;
        }

        var create = await PostJsonAuthenticatedAsync(_requesterA2,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "Family emergency" });
        create.status.Should().Be(HttpStatusCode.Created);
        var requestId = create.json.GetProperty("id").GetGuid();

        var approve = await PostJsonAuthenticatedAsync(_approverA,
            $"/api/v1/attendance/work-area-change-requests/{requestId}/approve", new { reviewComment = (string?)null });
        approve.status.Should().Be(HttpStatusCode.OK);

        await using (var db = OpenScopedDb())
        {
            var record = await db.AttendanceRecords.AsNoTracking().SingleAsync(x => x.Id == attendanceRecordId);
            record.ExpectedWorkArea.Should().Be("remote");
            record.ActualStart.Should().Be(actualStart);
            record.ActualEnd.Should().BeNull();
            record.AttendanceSource.Should().Be("web");
        }

        var today = await GetJsonAuthenticatedAsync(_requesterA2, "/api/v1/attendance/time-tracking/today");
        today.GetProperty("expectedWorkMode").GetString().Should().Be("remote");
        today.GetProperty("expectedWorkAreaSource").GetString().Should().Be(TodaySnapshotSource.ExpectedWorkAreaSourceAttendanceSnapshot);

        var history = await GetJsonAuthenticatedAsync(_requesterA2,
            $"/api/v1/attendance/time-tracking/history?from={WorkDate:yyyy-MM-dd}&to={WorkDate:yyyy-MM-dd}");
        history.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("attendanceRecordId").GetGuid() == attendanceRecordId)
            .GetProperty("expectedWorkMode").GetString().Should().Be("remote");
    }

    [Fact]
    public async Task RejectedAndCancelledRequests_DoNotAffectToday()
    {
        var firstRequest = await PostJsonAuthenticatedAsync(_requesterA3,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "Reason one" });
        firstRequest.status.Should().Be(HttpStatusCode.Created);
        var firstId = firstRequest.json.GetProperty("id").GetGuid();

        var reject = await PostJsonAuthenticatedAsync(_approverA,
            $"/api/v1/attendance/work-area-change-requests/{firstId}/reject", new { reviewComment = "Not approved for this date" });
        reject.status.Should().Be(HttpStatusCode.OK);
        reject.json.GetProperty("status").GetString().Should().Be(WorkAreaChangeRequest.StatusRejected);

        var todayAfterReject = await GetJsonAuthenticatedAsync(_requesterA3, "/api/v1/attendance/time-tracking/today");
        todayAfterReject.GetProperty("expectedWorkMode").GetString().Should().Be("onsite");

        // A new request is allowed once the previous one reached a terminal state.
        var secondRequest = await PostJsonAuthenticatedAsync(_requesterA3,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "Reason two" });
        secondRequest.status.Should().Be(HttpStatusCode.Created);
        var secondId = secondRequest.json.GetProperty("id").GetGuid();

        var cancel = await PostJsonAuthenticatedAsync(_requesterA3,
            $"/api/v1/attendance/work-area-change-requests/{secondId}/cancel", new { });
        cancel.status.Should().Be(HttpStatusCode.OK);
        cancel.json.GetProperty("status").GetString().Should().Be(WorkAreaChangeRequest.StatusCancelled);

        var todayAfterCancel = await GetJsonAuthenticatedAsync(_requesterA3, "/api/v1/attendance/time-tracking/today");
        todayAfterCancel.GetProperty("expectedWorkMode").GetString().Should().Be("onsite");
    }

    [Fact]
    public async Task Unauthenticated_And_MissingOrInvalidCsrf_AreRejected()
    {
        var unauthenticatedToday = await SendAsync(HttpMethod.Get, _requesterA.Host,
            "/api/v1/attendance/time-tracking/today", body: null);
        unauthenticatedToday.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthenticatedClockIn = await SendAsync(HttpMethod.Post, _requesterA.Host,
            "/api/v1/attendance/time-tracking/clock-in", new { source = "web" });
        unauthenticatedClockIn.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthenticatedCreate = await SendAsync(HttpMethod.Post, _requesterA.Host,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "No session" });
        unauthenticatedCreate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var missingCsrf = await SendAsync(HttpMethod.Post, _requesterA.Host,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "Missing token" },
            cookie: _requesterA.SessionCookie);
        missingCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var invalidCsrf = await SendAsync(HttpMethod.Post, _requesterA.Host,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "Invalid token" },
            cookie: _requesterA.SessionCookie, csrfToken: "not-the-real-token");
        invalidCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantIsolation_CannotSeeOrApproveAnotherTenantsRequest()
    {
        var create = await PostJsonAuthenticatedAsync(_requesterA,
            "/api/v1/attendance/work-area-change-requests",
            new { date = WorkDate, requestedWorkArea = "remote", reason = "Tenant isolation fixture" });
        create.status.Should().Be(HttpStatusCode.Created);
        var requestId = create.json.GetProperty("id").GetGuid();

        var inboxAsTenantB = await GetJsonAuthenticatedAsync(_approverB, "/api/v1/attendance/work-area-change-requests/approvals");
        inboxAsTenantB.GetProperty("items").EnumerateArray()
            .Should().NotContain(x => x.GetProperty("id").GetGuid() == requestId);

        var approveAsTenantB = await PostJsonAuthenticatedAsync(_approverB,
            $"/api/v1/attendance/work-area-change-requests/{requestId}/approve", new { reviewComment = (string?)null });
        approveAsTenantB.status.Should().Be(HttpStatusCode.NotFound);

        var todayAsRequesterA = await GetJsonAuthenticatedAsync(_requesterA, "/api/v1/attendance/time-tracking/today");
        todayAsRequesterA.GetProperty("expectedWorkMode").GetString().Should().Be("onsite",
            "tenant B's failed cross-tenant approve attempt must not affect tenant A's state");
    }

    // ── Assertion helpers ────────────────────────────────────────────────────

    private static void AssertAllowedMethods(JsonElement today, bool web, bool tray, bool photo)
    {
        var methods = today.GetProperty("allowedClockInMethods");
        methods.GetProperty("web").GetBoolean().Should().Be(web);
        methods.GetProperty("desktopTray").GetBoolean().Should().Be(tray);
        methods.GetProperty("photoRequired").GetBoolean().Should().Be(photo);
    }

    // ── Fixture setup helpers ────────────────────────────────────────────────

    private ApplicationDbContext OpenScopedDb()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    private async Task ConfigureLegalEntityGeneralSettingsAsync(Guid legalEntityId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var legalEntity = await db.LegalEntities.SingleAsync(x => x.Id == legalEntityId);
        legalEntity.Timezone = "Asia/Colombo";
        legalEntity.WorkStartTime = new TimeOnly(9, 0);
        legalEntity.WorkEndTime = new TimeOnly(18, 0);
        legalEntity.BreakDurationMinutes = 60;
        // Every day is a working day so schedule resolution never depends on which real
        // weekday the suite happens to run on (WorkDate is resolved from the real clock).
        legalEntity.StandardWorkingDays = "[1,2,3,4,5,6,7]";
        await db.SaveChangesAsync();
    }

    private async Task SeedClockInPolicyAsync(Guid tenantId, Guid legalEntityId, Guid createdById)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ClockInPolicies.Add(new ClockInPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = "Full Company Policy",
            ScopeType = ClockInPolicy.ScopeFullCompany,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            EffectiveTo = null,
            LocationVerificationRequired = false,
            // Onsite and Remote branches are deliberately distinguished on tray/photo so Today's
            // AllowedClockInMethods response provably switches branch on the approved override,
            // while both keep Web enabled (the approval-after-clock-in scenario clocks in as
            // On-site first via web, before any override exists).
            OnsiteWebEnabled = true,
            OnsiteTrayEnabled = false,
            OnsiteBiometricEnabled = false,
            OnsitePhotoRequired = false,
            RemoteWebEnabled = true,
            RemoteTrayEnabled = true,
            RemoteBiometricEnabled = false,
            RemotePhotoRequired = true,
            EitherWebEnabled = true,
            EitherTrayEnabled = false,
            EitherBiometricEnabled = false,
            EitherPhotoRequired = false,
            FieldWebEnabled = false,
            FieldTrayEnabled = false,
            FieldBiometricEnabled = false,
            FieldPhotoRequirement = ClockInPolicy.FieldPhotoOff,
            CorrectionRequiresApproval = false,
            IsActive = true,
            CreatedById = createdById,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPositionAsync(Guid tenantId, Guid legalEntityId, string name, Guid? reportsToPositionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var position = new Position
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = name,
            PositionType = Position.TypeUnique,
            MaxOccupancy = 1,
            ReportsToPositionId = reportsToPositionId,
            IsActive = true
        };
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        return position.Id;
    }

    private async Task AssignPrimaryPositionAsync(
        Guid tenantId, Guid employeeId, Guid positionId, Guid createdById, Guid? reportsToEmployeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = PositionAssignmentRepositoryTestSupport.CreateRepository(db);
        var assignmentId = await repository.TryCreateActiveAssignmentAsync(
            tenantId, employeeId, positionId, new DateOnly(2020, 1, 1), createdById, reportsToEmployeeId);
        assignmentId.Should().NotBeNull("the fixture's position/occupancy setup must allow the assignment to be created");
    }

    private async Task GrantPermissionAsync(Guid tenantId, Guid userId, string permissionCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var permission = await db.Permissions.SingleAsync(p => p.Code == permissionCode);
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = $"Role-{roleId:N}"[..20],
            CreatedById = userId
        });
        db.RolePermissions.Add(new RolePermission
        {
            TenantId = tenantId,
            RoleId = roleId,
            PermissionId = permission.Id
        });
        db.UserRoles.Add(new UserRole
        {
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedBy = userId
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> GetUserIdByEmailAsync(Guid tenantId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.TenantId == tenantId && u.Email == email);
        return user.Id;
    }

    // ── Provisioning and HTTP boilerplate (mirrors AttendanceCorrectionsIntegrationTests) ────

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

    private async Task<(TenantSession Session, Guid EmployeeId, Guid UserId)> SeedEmployeeFixtureUserAsync(
        Guid tenantId, string host, string email, Guid legalEntityId, string employeeNumber, int workModeId)
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
            LastName = "Employee",
            PasswordHash = hasher.Hash(FixtureUserPassword),
            IsActive = true,
            EmailVerified = true,
            MustChangePassword = false,
            PasswordSetByAdmin = false,
            CreatedAt = now,
            CreatedById = userId
        });

        var employeeId = Guid.NewGuid();
        db.Add(new Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            UserId = userId,
            EmployeeNumber = employeeNumber,
            FirstName = "Fixture",
            LastName = "Employee",
            Email = email,
            LegalEntityId = legalEntityId,
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = workModeId,
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

        var session = await LoginViaBaseHostAsync(host, email, FixtureUserPassword);
        return (session, employeeId, userId);
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
        var list = await GetJsonAuthenticatedAsync(session, "/api/v1/org/legal-entities");
        var primary = list.EnumerateArray().Single(i => i.GetProperty("isPrimary").GetBoolean());
        return primary.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetJsonAuthenticatedAsync(TenantSession session, string path)
    {
        var response = await SendAsync(HttpMethod.Get, session.Host, path, body: null, cookie: session.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {path} failed for host {session.Host}");
        return await ReadJsonAsync(response);
    }

    private async Task<(HttpStatusCode status, JsonElement json)> PostJsonAuthenticatedAsync(
        TenantSession session, string path, object body)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host, path, body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        var json = await ReadJsonAsync(response);
        return (response.StatusCode, json);
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
            }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan missing).");
    }

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
