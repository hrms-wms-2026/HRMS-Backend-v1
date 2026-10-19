using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Inbox;

/// <summary>
/// The chat of an attendance request: the requester and the approver resolved now (the same route the approve
/// command checks, so a changed reporting line moves the chat). HR with attendance:read may view the request and its
/// chat from their own scope, and take the investigation export.
/// </summary>
public sealed class AttendanceConversationSubjectResolver : IConversationSubjectResolver
{
    private readonly IAttendanceCorrectionRepository _corrections;
    private readonly IWorkAreaChangeRequestRepository _workAreas;
    private readonly ILocationChangeRequestRepository _locations;
    private readonly IDeviceChangeRequestRepository _devices;
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeAuthorityResolverFactory _authority;
    private readonly IPermissionRepository _permissions;
    private readonly IDateTimeProvider _clock;
    private readonly ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertRepository _alerts;

    public AttendanceConversationSubjectResolver(
        IAttendanceCorrectionRepository corrections, IWorkAreaChangeRequestRepository workAreas, ILocationChangeRequestRepository locations,
        IDeviceChangeRequestRepository devices, IEmployeeRepository employees, IEmployeeAuthorityResolverFactory authority,
        IPermissionRepository permissions, IDateTimeProvider clock,
        ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertRepository alerts)
    {
        _corrections = corrections;
        _workAreas = workAreas;
        _locations = locations;
        _devices = devices;
        _employees = employees;
        _authority = authority;
        _permissions = permissions;
        _clock = clock;
        _alerts = alerts;
    }

    public string Source => AttendanceInboxKinds.Source;

    public async Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
    {
        if (await LoadAsync(tenantId, subject, ct) is not { } found)
            return null;

        var people = new List<Guid> { found.EmployeeId };
        var legalEntityId = await LegalEntityAsync(tenantId, found, ct);
        if (legalEntityId is Guid le)
        {
            var route = await _authority.CreateForTenant(tenantId).ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                found.EmployeeId, le, AttendanceInboxKinds.ApprovePermission, AttendanceInboxKinds.Purposes[subject.Kind]), ct);
            if (route.IsSuccess && route.Value is { } approver)
                people.Add(approver.ApproverEmployeeId);
        }
        // An alert is about the employee (they may post a justification); a request only has a requester.
        var subjectEmployee = subject.Kind == AttendanceInboxKinds.Alert ? found.EmployeeId : (Guid?)null;
        return new ConversationSubject(people.Distinct().ToList(), found.Title, null, subjectEmployee);
    }

    private async Task<Guid?> LegalEntityAsync(Guid tenantId, (Guid EmployeeId, Guid? LegalEntityId, string Title) found, CancellationToken ct)
        => found.LegalEntityId ?? (await _employees.GetByIdAsync(tenantId, found.EmployeeId, ct))?.LegalEntityId;

    private async Task<(Guid EmployeeId, Guid? LegalEntityId, string Title)?> LoadAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
        => subject.Kind switch
        {
            AttendanceInboxKinds.Correction => await _corrections.GetByIdAsync(tenantId, subject.Id, ct) is { } c
                ? (c.EmployeeId, c.LegalEntityId, $"Attendance correction · {c.WorkDate:dd MMM yyyy}") : null,
            AttendanceInboxKinds.WorkModeChange => await _workAreas.GetByIdAsync(tenantId, subject.Id, ct) is { } w
                ? (w.EmployeeId, w.LegalEntityId, $"Work mode change · {w.Date:dd MMM yyyy}") : null,
            AttendanceInboxKinds.LocationChange => await _locations.GetByIdAsync(tenantId, subject.Id, ct) is { } l
                ? (l.EmployeeId, l.LegalEntityId, "Work location change") : null,
            AttendanceInboxKinds.DeviceChange => await _devices.GetTrackedByIdAsync(tenantId, subject.Id, ct) is { } d
                ? (d.EmployeeId, d.LegalEntityId, $"Device change · {d.NewDeviceName}") : null,
            AttendanceInboxKinds.Alert => await _alerts.GetByIdAsync(tenantId, subject.Id, ct) is { } a
                ? (a.EmployeeId, a.LegalEntityId, $"{AttendanceAlertInbox.SignalLabel(a.SignalKey)} · {a.WorkDate:dd MMM yyyy}") : null,
            _ => null,
        };

    public Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => ReaderCoversAsync(tenantId, subject, employeeId, ct);

    public Task<bool> CanReviewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => ReaderCoversAsync(tenantId, subject, employeeId, ct);

    /// <summary>HR scope: attendance:read and the item's employee in the reader's coverage.</summary>
    private async Task<bool> ReaderCoversAsync(Guid tenantId, ItemRef subject, Guid readerEmployeeId, CancellationToken ct)
    {
        var reader = await _employees.GetByIdAsync(tenantId, readerEmployeeId, ct);
        if (reader is null || reader.UserId == Guid.Empty)
            return false;
        var readers = await _permissions.ListUserIdsWithPermissionCodeAsync(tenantId, AttendanceInboxKinds.ReadPermission, _clock.UtcNow, ct);
        if (!readers.Contains(reader.UserId) || await LoadAsync(tenantId, subject, ct) is not { } found)
            return false;
        return await AttendanceReadCoverage.CoversAsync(_authority.CreateForTenant(tenantId), reader.UserId,
            await LegalEntityAsync(tenantId, found, ct), found.EmployeeId, ct);
    }
}
