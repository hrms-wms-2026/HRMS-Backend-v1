using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using ONEVO.Api.Hubs;
using ONEVO.Tests.Integration.Support;
using Xunit;
using Xunit.Abstractions;
using static ONEVO.Tests.Integration.Security.TenantAndObjectAccess.TenantAndObjectAccessFixture;

namespace ONEVO.Tests.Integration.Security.TenantAndObjectAccess;

/// <summary>
/// Task 27: tries every module's main read-by-id and write-by-id endpoint with an id the caller must
/// not reach - an object in another tenant, or a colleague's object in the same tenant - and expects
/// 403 or 404 with none of the victim's data in the body. Each probe is its own test case so the
/// results table in docs/reports/security/ maps one row to one test.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TenantAndObjectAccessTests : IClassFixture<TenantAndObjectAccessFixture>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly TenantAndObjectAccessFixture _f;
    private readonly ITestOutputHelper _output;

    public TenantAndObjectAccessTests(TenantAndObjectAccessFixture fixture, ITestOutputHelper output)
    {
        _f = fixture;
        _output = output;
    }

    private sealed record Probe(
        Func<TenantAndObjectAccessFixture, TenantSession> Actor,
        HttpMethod Method,
        Func<TenantAndObjectAccessFixture, string> Path,
        Func<TenantAndObjectAccessFixture, object?>? Body = null,
        string Marker = TenantBMarker);

    private static readonly HttpMethod Get = HttpMethod.Get;
    private static readonly HttpMethod Put = HttpMethod.Put;
    private static readonly HttpMethod Post = HttpMethod.Post;
    private static readonly HttpMethod Patch = HttpMethod.Patch;
    private static readonly HttpMethod Delete = HttpMethod.Delete;

    // ── 1. Tenant A's owner (every permission) against tenant B's ids ────────

    private static readonly Dictionary<string, Probe> CrossTenantProbes = new()
    {
        ["CoreHr: GET employee"] = new(f => f.OwnerA, Get, f => $"/api/v1/employees/{f.B.OwnerEmployeeId}"),
        ["CoreHr: GET employee detail"] = new(f => f.OwnerA, Get, f => $"/api/v1/employees/{f.B.OwnerEmployeeId}/detail"),
        ["CoreHr: GET employee time-off overview"] = new(f => f.OwnerA, Get, f => $"/api/v1/employees/{f.B.OwnerEmployeeId}/overview/time-off"),
        ["CoreHr: GET employee documents"] = new(f => f.OwnerA, Get, f => $"/api/v1/employees/{f.B.OwnerEmployeeId}/documents"),
        ["CoreHr: PUT employee job details"] = new(f => f.OwnerA, Put, f => $"/api/v1/employees/{f.B.OwnerEmployeeId}/job-details",
            _ => new { employeeNumber = "HIJACK-1", employmentTypeCode = "full_time", workModeId = (Guid?)null }),
        ["Org: GET legal entity settings"] = new(f => f.OwnerA, Get, f => $"/api/v1/org/legal-entities/{f.B.LegalEntityId}/general-settings"),
        ["Org: DELETE legal entity logo"] = new(f => f.OwnerA, Delete, f => $"/api/v1/org/legal-entities/{f.B.LegalEntityId}/logo"),
        ["Org: PUT own logo to tenant B's file"] = new(f => f.OwnerA, Put, f => $"/api/v1/org/legal-entities/{f.A.LegalEntityId}/logo",
            f => new { fileId = f.B.LogoFileId }),
        ["Org: GET department"] = new(f => f.OwnerA, Get, f => $"/api/v1/org/legal-entities/{f.B.LegalEntityId}/departments/{f.B.DepartmentId}"),
        ["Org: GET department under own legal entity"] = new(f => f.OwnerA, Get, f => $"/api/v1/org/legal-entities/{f.A.LegalEntityId}/departments/{f.B.DepartmentId}"),
        ["Org: PUT department"] = new(f => f.OwnerA, Put, f => $"/api/v1/org/legal-entities/{f.B.LegalEntityId}/departments/{f.B.DepartmentId}",
            _ => new { name = "Renamed by tenant A", code = (string?)null, parentDepartmentId = (Guid?)null, headPositionId = (Guid?)null }),
        ["Org: DELETE department"] = new(f => f.OwnerA, Delete, f => $"/api/v1/org/legal-entities/{f.B.LegalEntityId}/departments/{f.B.DepartmentId}"),
        ["Roles: GET role"] = new(f => f.OwnerA, Get, f => $"/api/v1/roles/{f.B.OwnerRoleId}"),
        ["Roles: PATCH role"] = new(f => f.OwnerA, Patch, f => $"/api/v1/roles/{f.B.OwnerRoleId}", _ => new { name = "Hijacked", description = "x" }),
        ["Roles: PUT role permissions"] = new(f => f.OwnerA, Put, f => $"/api/v1/roles/{f.B.OwnerRoleId}/permissions", _ => new { permissionIds = Array.Empty<Guid>() }),
        ["Leave: GET leave type"] = new(f => f.OwnerA, Get, f => $"/api/v1/leave/types/{f.B.LeaveTypeId}"),
        ["Leave: PUT leave type"] = new(f => f.OwnerA, Put, f => $"/api/v1/leave/types/{f.B.LeaveTypeId}", _ => LeaveTypeBody("Hijacked", "HIJ")),
        ["Leave: GET request detail"] = new(f => f.OwnerA, Get, f => $"/api/v1/leave/requests/{f.B.LeaveRequestId}/detail"),
        ["Leave: GET request approval"] = new(f => f.OwnerA, Get, f => $"/api/v1/leave/requests/{f.B.LeaveRequestId}/approval"),
        ["Leave: POST approve request"] = new(f => f.OwnerA, Post, f => $"/api/v1/leave/requests/{f.B.LeaveRequestId}/approve", _ => new { comment = "x" }),
        ["Leave: POST cancel request"] = new(f => f.OwnerA, Post, f => $"/api/v1/leave/requests/{f.B.LeaveRequestId}/cancel", _ => new { reason = "x" }),
        ["Work: GET project"] = new(f => f.OwnerA, Get, f => $"/api/v1/work/projects/{f.B.ProjectId}"),
        ["Work: GET project banner"] = new(f => f.OwnerA, Get, f => $"/api/v1/work/projects/{f.B.ProjectId}/banner"),
        ["Work: DELETE project"] = new(f => f.OwnerA, Delete, f => $"/api/v1/work/projects/{f.B.ProjectId}"),
        ["Work: GET objective"] = new(f => f.OwnerA, Get, f => $"/api/v1/work/objectives/{f.B.ObjectiveId}"),
        ["Work: GET task"] = new(f => f.OwnerA, Get, f => $"/api/v1/work/tasks/{f.B.TaskId}"),
        ["Work: PATCH task"] = new(f => f.OwnerA, Patch, f => $"/api/v1/work/tasks/{f.B.TaskId}", _ => new { title = "Hijacked", priority = "high" }),
        ["Work: DELETE task"] = new(f => f.OwnerA, Delete, f => $"/api/v1/work/tasks/{f.B.TaskId}"),
        ["Work: GET task file"] = new(f => f.OwnerA, Get, f => $"/api/v1/work/tasks/files/{f.B.TaskFileId}"),
        ["Storage: GET pending file"] = new(f => f.OwnerA, Get, f => $"/api/v1/files/{f.B.PendingFileId}"),
        ["Storage: GET logo file"] = new(f => f.OwnerA, Get, f => $"/api/v1/files/{f.B.LogoFileId}"),
        ["Storage: GET task file"] = new(f => f.OwnerA, Get, f => $"/api/v1/files/{f.B.TaskFileId}"),
        ["Storage: DELETE file"] = new(f => f.OwnerA, Delete, f => $"/api/v1/files/{f.B.PendingFileId}"),
        ["Support: GET ticket"] = new(f => f.OwnerA, Get, f => $"/api/v1/support/tickets/{f.B.SupportTicketId}"),
        ["Support: PUT ticket"] = new(f => f.OwnerA, Put, f => $"/api/v1/support/tickets/{f.B.SupportTicketId}",
            _ => new { subject = "Hijacked", description = "Hijacked", priority = "low", category = "general" }),
        ["Support: POST ticket comment"] = new(f => f.OwnerA, Post, f => $"/api/v1/support/tickets/{f.B.SupportTicketId}/comments", _ => new { body = "x" }),
        ["Dashboard: GET bootstrap"] = new(f => f.OwnerA, Get, f => $"/api/v1/dashboards/{f.B.DashboardId}/bootstrap"),
        ["Dashboard: GET available widgets"] = new(f => f.OwnerA, Get, f => $"/api/v1/dashboards/{f.B.DashboardId}/widgets/available"),
        ["Dashboard: POST reset"] = new(f => f.OwnerA, Post, f => $"/api/v1/dashboards/{f.B.DashboardId}/reset", _ => new { }),
        ["Conversations: GET messages"] = new(f => f.OwnerA, Get, f => $"/api/v1/conversations/{f.B.ConversationId}/messages"),
        ["Conversations: POST message"] = new(f => f.OwnerA, Post, f => $"/api/v1/conversations/{f.B.ConversationId}/messages", _ => new { bodyHtml = "<p>x</p>" }),
        ["Attendance: GET day detail"] = new(f => f.OwnerA, Get, f => $"/api/v1/attendance/time-tracking/history-detail?employeeId={f.B.OwnerEmployeeId}&date={Today:yyyy-MM-dd}"),
    };

    public static IEnumerable<object[]> CrossTenantProbeNames => CrossTenantProbes.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(CrossTenantProbeNames))]
    public async Task CrossTenant_ObjectId_IsNotReachable(string probe)
        => await AssertRefusedAsync(CrossTenantProbes[probe], probe);

    // ── 2. Colleague and other-team ids inside tenant A ──────────────────────

    private static readonly Dictionary<string, Probe> ObjectProbes = new()
    {
        ["CoreHr: employee GET colleague"] = new(f => f.Employee, Get, f => $"/api/v1/employees/{f.ColleagueEmployeeId}", Marker: ColleagueMarker),
        ["CoreHr: employee GET colleague detail"] = new(f => f.Employee, Get, f => $"/api/v1/employees/{f.ColleagueEmployeeId}/detail", Marker: ColleagueMarker),
        ["CoreHr: employee GET colleague time-off"] = new(f => f.Employee, Get, f => $"/api/v1/employees/{f.ColleagueEmployeeId}/overview/time-off", Marker: ColleagueMarker),
        ["CoreHr: employee GET colleague documents"] = new(f => f.Employee, Get, f => $"/api/v1/employees/{f.ColleagueEmployeeId}/documents", Marker: ColleagueMarker),
        ["CoreHr: other-team manager GET employee"] = new(f => f.OtherTeamManager, Get, f => $"/api/v1/employees/{f.ColleagueEmployeeId}", Marker: ColleagueMarker),
        ["CoreHr: other-team manager GET detail"] = new(f => f.OtherTeamManager, Get, f => $"/api/v1/employees/{f.ColleagueEmployeeId}/detail", Marker: ColleagueMarker),
        ["CoreHr: other-team manager GET attendance overview"] = new(f => f.OtherTeamManager, Get, f => $"/api/v1/employees/{f.ColleagueEmployeeId}/overview/attendance", Marker: ColleagueMarker),
        ["CoreHr: other-team manager GET activity overview"] = new(f => f.OtherTeamManager, Get, f => $"/api/v1/employees/{f.ColleagueEmployeeId}/overview/activity", Marker: ColleagueMarker),
        ["CoreHr: other-team manager PUT job details"] = new(f => f.OtherTeamManager, Put, f => $"/api/v1/employees/{f.ColleagueEmployeeId}/job-details",
            _ => new { employeeNumber = "HIJACK-1", employmentTypeCode = "full_time", workModeId = (Guid?)null }, ColleagueMarker),
        ["Attendance: employee GET colleague day detail"] = new(f => f.Employee, Get, f => $"/api/v1/attendance/time-tracking/history-detail?employeeId={f.ColleagueEmployeeId}&date={Today:yyyy-MM-dd}", Marker: ColleagueMarker),
        ["Attendance: other-team manager GET day detail"] = new(f => f.OtherTeamManager, Get, f => $"/api/v1/attendance/time-tracking/history-detail?employeeId={f.ColleagueEmployeeId}&date={Today:yyyy-MM-dd}", Marker: ColleagueMarker),
        ["Leave: employee GET colleague request detail"] = new(f => f.Employee, Get, f => $"/api/v1/leave/requests/{f.ColleagueLeaveRequestId}/detail", Marker: ColleagueMarker),
        ["Leave: employee POST cancel colleague request"] = new(f => f.Employee, Post, f => $"/api/v1/leave/requests/{f.ColleagueLeaveRequestId}/cancel", _ => new { reason = "x" }, ColleagueMarker),
        ["Leave: other-team manager GET approval"] = new(f => f.OtherTeamManager, Get, f => $"/api/v1/leave/requests/{f.ColleagueLeaveRequestId}/approval", Marker: ColleagueMarker),
        ["Leave: other-team manager POST approve"] = new(f => f.OtherTeamManager, Post, f => $"/api/v1/leave/requests/{f.ColleagueLeaveRequestId}/approve", _ => new { comment = "x" }, ColleagueMarker),
        ["Storage: employee GET colleague pending file"] = new(f => f.Employee, Get, f => $"/api/v1/files/{f.ColleaguePendingFileId}", Marker: ColleagueMarker),
        ["Storage: employee DELETE colleague pending file"] = new(f => f.Employee, Delete, f => $"/api/v1/files/{f.ColleaguePendingFileId}", Marker: ColleagueMarker),
        ["Storage: HR GET colleague pending file"] = new(f => f.Hr, Get, f => $"/api/v1/files/{f.ColleaguePendingFileId}", Marker: ColleagueMarker),
        ["Dashboard: employee GET colleague bootstrap"] = new(f => f.Employee, Get, f => $"/api/v1/dashboards/{f.ColleagueDashboardId}/bootstrap", Marker: ColleagueMarker),
        ["Dashboard: employee POST reset colleague dashboard"] = new(f => f.Employee, Post, f => $"/api/v1/dashboards/{f.ColleagueDashboardId}/reset", _ => new { }, ColleagueMarker),
        ["Work: non-member GET project"] = new(f => f.Employee, Get, f => $"/api/v1/work/projects/{f.A.ProjectId}", Marker: "Project Alpha"),
        ["Work: non-member GET project banner"] = new(f => f.Employee, Get, f => $"/api/v1/work/projects/{f.A.ProjectId}/banner", Marker: "Project Alpha"),
        ["Work: non-member GET objective"] = new(f => f.Employee, Get, f => $"/api/v1/work/objectives/{f.A.ObjectiveId}", Marker: "Project Alpha"),
        ["Work: non-member GET task"] = new(f => f.Employee, Get, f => $"/api/v1/work/tasks/{f.A.TaskId}", Marker: "Task Alpha"),
        ["Work: non-member PATCH task"] = new(f => f.Employee, Patch, f => $"/api/v1/work/tasks/{f.A.TaskId}", _ => new { title = "Hijacked", priority = "high" }, "Task Alpha"),
        ["Work: non-member GET task file"] = new(f => f.Employee, Get, f => $"/api/v1/work/tasks/files/{f.A.TaskFileId}", Marker: "Alpha.pdf"),
        ["Storage: non-member GET task file"] = new(f => f.Employee, Get, f => $"/api/v1/files/{f.A.TaskFileId}", Marker: "Alpha.pdf"),
        ["Conversations: non-participant GET messages"] = new(f => f.Employee, Get, f => $"/api/v1/conversations/{f.A.ConversationId}/messages", Marker: "Task Alpha"),
        ["Roles: employee PATCH owner role"] = new(f => f.Employee, Patch, f => $"/api/v1/roles/{f.A.OwnerRoleId}", _ => new { name = "Hijacked", description = "x" }, "Owner"),
    };

    public static IEnumerable<object[]> ObjectProbeNames => ObjectProbes.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(ObjectProbeNames))]
    public async Task SameTenant_ObjectIdWithoutAccess_IsNotReachable(string probe)
        => await AssertRefusedAsync(ObjectProbes[probe], probe);

    // ── 3. Positive controls: the same endpoints work for the rightful user ──

    [Fact]
    public async Task PositiveControls_RightfulCallerGetsTheObject()
    {
        (TenantSession Actor, string Path)[] allowed =
        [
            (_f.Employee, $"/api/v1/employees/{_f.EmployeeEmployeeId}"),
            (_f.Colleague, $"/api/v1/files/{_f.ColleaguePendingFileId}"),
            (_f.Colleague, $"/api/v1/dashboards/{_f.ColleagueDashboardId}/bootstrap"),
            (_f.Hr, $"/api/v1/leave/requests/{_f.ColleagueLeaveRequestId}/detail"),
            (_f.OwnerA, $"/api/v1/work/tasks/{_f.A.TaskId}"),
            (_f.OwnerA, $"/api/v1/work/tasks/files/{_f.A.TaskFileId}"),
            (_f.OwnerA, $"/api/v1/work/projects/{_f.A.ProjectId}/banner"),
            (_f.OwnerA, $"/api/v1/files/{_f.A.LogoFileId}"),
            (_f.OwnerA, $"/api/v1/leave/types/{_f.A.LeaveTypeId}"),
            (_f.OwnerA, $"/api/v1/roles/{_f.A.OwnerRoleId}"),
            (_f.OwnerA, $"/api/v1/support/tickets/{_f.A.SupportTicketId}"),
            (_f.OwnerA, $"/api/v1/org/legal-entities/{_f.A.LegalEntityId}/departments/{_f.A.DepartmentId}"),
            (_f.OwnerB, $"/api/v1/work/tasks/{_f.B.TaskId}"),
            (_f.OwnerB, $"/api/v1/files/{_f.B.LogoFileId}"),
        ];

        var failures = new List<string>();
        foreach (var (actor, path) in allowed)
        {
            var response = await _f.SendAsync(actor, Get, path);
            if (response.StatusCode != HttpStatusCode.OK)
                failures.Add($"{path} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task WritesFromOtherTenant_LeftTenantBObjectsUnchanged()
    {
        // Runs the cross-tenant writes again, then checks tenant B still sees its own data.
        foreach (var name in CrossTenantProbes.Keys.Where(k => CrossTenantProbes[k].Method != Get))
            await SendProbeAsync(CrossTenantProbes[name]);

        (string Path, string Expected)[] checks =
        [
            ($"/api/v1/work/tasks/{_f.B.TaskId}", $"Task {TenantBMarker}"),
            ($"/api/v1/leave/types/{_f.B.LeaveTypeId}", $"Leave {TenantBMarker}"),
            ($"/api/v1/org/legal-entities/{_f.B.LegalEntityId}/departments/{_f.B.DepartmentId}", $"Dept {TenantBMarker}"),
            ($"/api/v1/support/tickets/{_f.B.SupportTicketId}", $"Ticket {TenantBMarker}"),
            ($"/api/v1/roles/{_f.B.OwnerRoleId}", "Owner"),
        ];

        foreach (var (path, expected) in checks)
        {
            var response = await _f.SendAsync(_f.OwnerB, Get, path);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path}: {text}");
            text.Should().Contain(expected, path);
        }

        var settings = await ReadJsonAsync(await _f.SendAsync(_f.OwnerB, Get, $"/api/v1/org/legal-entities/{_f.B.LegalEntityId}/general-settings"));
        settings.GetProperty("logoFileId").GetGuid().Should().Be(_f.B.LogoFileId);
    }

    // ── 4. Ids passed in a body or a filter ──────────────────────────────────

    [Fact]
    public async Task CreateTask_WithAnotherUsersPendingUpload_DoesNotLinkIt()
    {
        var otherTenantFile = await _f.UploadTaskPendingFileAsync(_f.OwnerB, $"{TenantBMarker}-attach.pdf");
        var colleagueFile = await _f.UploadTaskPendingFileAsync(_f.Colleague, $"{ColleagueMarker}-attach.pdf");

        foreach (var fileId in new[] { otherTenantFile, colleagueFile })
        {
            var create = await _f.SendJsonAsync(_f.OwnerA, Post, $"/api/v1/work/objectives/{_f.A.ObjectiveId}/tasks", new
            {
                title = "Attach probe",
                categoryId = (Guid?)null,
                priority = "medium",
                attachmentFileIds = new[] { fileId }
            });
            var text = await create.Content.ReadAsStringAsync();
            text.Should().NotContain(TenantBMarker).And.NotContain(ColleagueMarker);

            if (create.IsSuccessStatusCode)
            {
                var taskId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();
                var task = await _f.SendAsync(_f.OwnerA, Get, $"/api/v1/work/tasks/{taskId}");
                (await task.Content.ReadAsStringAsync()).Should().NotContain(fileId.ToString());
            }

            var download = await _f.SendAsync(_f.OwnerA, Get, $"/api/v1/work/tasks/files/{fileId}");
            download.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task LinkLogo_WithAnotherTenantsPendingLogo_IsRefused()
    {
        var otherTenantLogo = await _f.UploadFileAsync(_f.OwnerB, "company_logo", "b-logo.png", "image/png", PngBytes);

        var response = await _f.SendJsonAsync(_f.OwnerA, Put, $"/api/v1/org/legal-entities/{_f.A.LegalEntityId}/logo",
            new { fileId = otherTenantLogo });

        ((int)response.StatusCode).Should().BeOneOf(400, 403, 404);
        var settings = await ReadJsonAsync(await _f.SendAsync(_f.OwnerA, Get, $"/api/v1/org/legal-entities/{_f.A.LegalEntityId}/general-settings"));
        settings.GetProperty("logoFileId").GetGuid().Should().Be(_f.A.LogoFileId);
    }

    [Fact]
    public async Task ListFilters_WithSomeoneElsesId_DoNotWidenTheResult()
    {
        var from = Today.AddDays(-30);
        var to = Today.AddDays(60);
        (TenantSession Actor, string Path, string Marker, string ForbiddenId)[] probes =
        [
            // The employee's own list ignores any employee id passed in.
            (_f.Employee, $"/api/v1/leave/requests/my?employeeId={_f.ColleagueEmployeeId}", ColleagueMarker, _f.ColleagueLeaveRequestId.ToString()),
            // Manager without coverage: the directory search must not reveal the colleague.
            (_f.OtherTeamManager, $"/api/v1/employees?search=Probe&employeeId={_f.ColleagueEmployeeId}", ColleagueMarker, _f.ColleagueEmployeeId.ToString()),
            (_f.OtherTeamManager, $"/api/v1/attendance/time-tracking/covered-history?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&employeeId={_f.ColleagueEmployeeId}", ColleagueMarker, _f.ColleagueEmployeeId.ToString()),
            // Tenant A's HR filtering by a tenant B employee id.
            (_f.Hr, $"/api/v1/leave/balance-audit?employeeId={_f.B.OwnerEmployeeId}", TenantBMarker, _f.B.OwnerEmployeeId.ToString()),
            (_f.Hr, $"/api/v1/leave/requests/all?employeeId={_f.B.OwnerEmployeeId}", TenantBMarker, _f.B.LeaveRequestId.ToString()),
            (_f.OwnerA, $"/api/v1/work/led-progress/tasks?employeeId={_f.B.OwnerEmployeeId}", TenantBMarker, _f.B.TaskId.ToString()),
            (_f.OwnerA, $"/api/v1/work/projects/{_f.B.ProjectId}/tasks", TenantBMarker, _f.B.TaskId.ToString()),
        ];

        var failures = new List<string>();
        foreach (var (actor, path, marker, forbiddenId) in probes)
        {
            var response = await _f.SendAsync(actor, Get, path);
            var text = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode >= 500)
                failures.Add($"{path} -> {(int)response.StatusCode}");
            if (text.Contains(marker, StringComparison.Ordinal) || text.Contains(forbiddenId, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{path} -> {(int)response.StatusCode} leaked: {text}");
        }

        failures.Should().BeEmpty();
    }

    // ── 5. Session and SignalR boundaries ────────────────────────────────────

    [Fact]
    public async Task TenantASession_OnTenantBHost_IsUnauthenticated()
    {
        string[] paths =
        [
            "/api/v1/employees",
            $"/api/v1/employees/{_f.B.OwnerEmployeeId}",
            $"/api/v1/work/tasks/{_f.B.TaskId}",
            $"/api/v1/files/{_f.B.LogoFileId}",
            "/api/v1/dashboards/me",
        ];

        foreach (var path in paths)
        {
            var response = await _f.SendAsync(_f.OwnerA, Get, path, host: _f.OwnerB.Host);
            // 401 (no session for this host) or 403 (session refused for this tenant): never data.
            response.StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], path);
            (await response.Content.ReadAsStringAsync()).Should().NotContain(TenantBMarker);
        }
    }

    [Fact]
    public async Task InboxHub_NegotiateIsBoundToTheSessionsOwnTenantHost()
    {
        var ownHost = await _f.SendAsync(_f.OwnerA, Post, "/hubs/inbox/negotiate?negotiateVersion=1");
        ownHost.StatusCode.Should().Be(HttpStatusCode.OK, await ownHost.Content.ReadAsStringAsync());

        var otherHost = await _f.SendAsync(_f.OwnerA, Post, "/hubs/inbox/negotiate?negotiateVersion=1", host: _f.OwnerB.Host);
        otherHost.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        var anonymous = await _f.SendAnonymousAsync(Post, _f.OwnerA.Host, "/hubs/inbox/negotiate?negotiateVersion=1");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AgentCommandsHub_RefusesABrowserSession()
    {
        var response = await _f.SendAsync(_f.OwnerA, Post, "/hubs/agent-commands/negotiate?negotiateVersion=1");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Both hubs choose the caller's groups on connect from the authenticated claims. A public hub
    /// method would be something a client can invoke - for example to join another user's or tenant's
    /// group - so any new one must come with its own access test.
    /// </summary>
    [Theory]
    [InlineData(typeof(InboxHub))]
    [InlineData(typeof(AgentCommandsHub))]
    public void Hubs_ExposeNoClientInvokableMethods(Type hubType)
    {
        var invokable = hubType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetBaseDefinition().DeclaringType == hubType)
            .Select(m => m.Name)
            .ToList();

        invokable.Should().BeEmpty($"{hubType.Name} groups must come only from the connection's claims");
        typeof(Hub).IsAssignableFrom(hubType).Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ProblemDetail(string text)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? string.Empty
                : doc.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }

    private async Task<HttpResponseMessage> SendProbeAsync(Probe probe)
    {
        var body = probe.Body?.Invoke(_f);
        return await _f.SendAsync(probe.Actor(_f), probe.Method, probe.Path(_f), body is null ? null : JsonContent.Create(body));
    }

    private async Task AssertRefusedAsync(Probe probe, string name)
    {
        using var response = await SendProbeAsync(probe);
        var text = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"PROBE|{name}|{probe.Method} {probe.Path(_f)}|{(int)response.StatusCode}|{ProblemDetail(text)}");

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.NotFound, HttpStatusCode.Forbidden],
            $"{name} must refuse the id, got {(int)response.StatusCode}: {text}");

        // An empty 404 is routing, not the handler: the probe would prove nothing.
        if (response.StatusCode == HttpStatusCode.NotFound)
            text.Should().NotBeNullOrWhiteSpace($"{name} returned an empty 404, so the route did not match");

        text.Should().NotContain(probe.Marker, $"{name} must not return the victim's data");
    }
}
