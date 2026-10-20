using System.Globalization;
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.AttendanceCorrections;
using ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests;
using ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Employee = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Application.Features.TimeAttendance.Inbox;

/// <summary>
/// Attendance requests in the Inbox (plan A1): corrections, work-mode (work-area) changes, work-location changes and
/// device changes. Received = pending requests the caller is the exact approver of right now (scope = act: the same
/// ResolveApprovalInboxScopeAsync check each module's approve command enforces, batched once per legal entity and
/// kind), only with attendance:approve - the permission the module endpoints require. Sent = the caller's own requests.
/// Actions run the module's own commands; the Inbox adds the journal row, with the before-state the 30-minute undo needs.
/// </summary>
public sealed class AttendanceInboxSourceProvider : IInboxSourceProvider, IInboxPersonCaseProvider
{
    public const int SentLimit = 200;
    public const int InboxLimit = 500;
    public const string MissingPermission = "You do not have permission to do this.";
    private const string UnknownAction = "This action is not available for this item.";

    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeAuthorityResolver _authority;
    private readonly IAttendanceCorrectionRepository _corrections;
    private readonly IWorkAreaChangeRequestRepository _workAreas;
    private readonly ILocationChangeRequestRepository _locations;
    private readonly IDeviceChangeRequestRepository _devices;
    private readonly IAttendanceReadRepository _attendance;
    private readonly IEmployeeWorkLocationRepository _workLocations;
    private readonly ITrayActivationRepository _tray;
    private readonly IWorkModeRepository _workModes;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ISender _sender;
    private readonly IInboxDecisionJournal _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationRepository _conversations;
    private readonly IDateTimeProvider _clock;
    private readonly AttendanceAlertInbox _alerts;

    public AttendanceInboxSourceProvider(
        ICurrentUser currentUser, IEmployeeRepository employees, IEmployeeAuthorityResolver authority,
        IAttendanceCorrectionRepository corrections, IWorkAreaChangeRequestRepository workAreas, ILocationChangeRequestRepository locations,
        IDeviceChangeRequestRepository devices, IAttendanceReadRepository attendance, IEmployeeWorkLocationRepository workLocations,
        ITrayActivationRepository tray, IWorkModeRepository workModes, ILegalEntityRepository legalEntities, ISender sender,
        IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IConversationRepository conversations, IDateTimeProvider clock,
        AttendanceAlertInbox alerts)
    {
        _currentUser = currentUser;
        _employees = employees;
        _authority = authority;
        _corrections = corrections;
        _workAreas = workAreas;
        _locations = locations;
        _devices = devices;
        _attendance = attendance;
        _workLocations = workLocations;
        _tray = tray;
        _workModes = workModes;
        _legalEntities = legalEntities;
        _sender = sender;
        _journal = journal;
        _unitOfWork = unitOfWork;
        _conversations = conversations;
        _clock = clock;
        _alerts = alerts;
    }

    public string Source => AttendanceInboxKinds.Source;
    public string Label => "Attendance";

    /// <summary>One request of any of the four kinds, in the shape the list needs.</summary>
    private sealed record Row(
        string Kind, Guid Id, Guid EmployeeId, Guid? LegalEntityId, string Status, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
        string Subtitle, string? CorrectionType);

    // ---- list -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct)
    {
        var wantRequests = query.Category is not { Count: > 0 } || query.Category.Contains(InboxCategories.Request);
        var wantAlerts = query.Category is not { Count: > 0 } || query.Category.Contains(InboxCategories.Alert);
        if (!wantRequests && !wantAlerts)
            return [];
        var caller = await _employees.GetByIdAsync(tenantId, callerEmployeeId, ct);
        if (caller is null)
            return [];
        var alerts = wantAlerts ? await _alerts.ListAsync(tenantId, caller.Id, await CallerLegalEntitiesAsync(tenantId, ct), ct) : [];
        if (!wantRequests)
            return alerts;

        var received = await ReceivedAsync(tenantId, ct);
        var sent = await SentAsync(tenantId, caller.Id, ct);
        var added = new List<Row>();
        foreach (var subject in await _conversations.ListAddedSubjectsAsync(tenantId, caller.Id, Source, ct))
        {
            if (await LoadRowAsync(tenantId, subject.Kind, subject.Id, ct) is { } row)
                added.Add(row);
        }

        var employeeIds = received.Concat(sent).Concat(added).Select(r => r.EmployeeId).Distinct().ToList();
        var people = employeeIds.Count == 0 ? new Dictionary<Guid, Employee>() : await _employees.ListByIdsAsync(tenantId, employeeIds, ct);

        var items = new Dictionary<(string, Guid), InboxItem>();
        foreach (var row in received)
            items.TryAdd((row.Kind, row.Id), Item(row, people, InboxDirections.Received, pending: true, ReceivedActions(row.Kind)));
        foreach (var row in sent)
            items.TryAdd((row.Kind, row.Id), Item(row, people, InboxDirections.Sent, IsPending(row.Status), SentActions(row)));
        foreach (var row in added)
            items.TryAdd((row.Kind, row.Id), Item(row, people, InboxDirections.Info, pending: false, []));
        foreach (var alert in alerts)
            items.TryAdd((alert.Kind, alert.Id), alert);
        return items.Values.ToList();
    }

    /// <summary>Pending requests the caller is the exact approver of, per legal entity the caller works in. Each kind:
    /// candidate employees with a pending request, narrowed in one ResolveApprovalInboxScopeAsync call.</summary>
    private async Task<List<Row>> ReceivedAsync(Guid tenantId, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(AttendanceInboxKinds.ApprovePermission))
            return [];
        var rows = new List<Row>();
        foreach (var legalEntityId in await CallerLegalEntitiesAsync(tenantId, ct))
        {
            // Corrections: the module lists them over the caller's visibility; the inbox narrows that to the exact approver.
            var visible = await _authority.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
                _currentUser.UserId, legalEntityId, AttendanceInboxKinds.ApprovePermission, IncludeSelf: false,
                EmployeeAuthorityPurpose.AttendanceCorrectionApproval), ct);
            var corrections = await _corrections.ListApprovalInboxAsync(
                tenantId, legalEntityId, visible.EmployeeIds, null, null, AttendanceCorrection.StatusPending, ct);
            var correctionScope = await ScopeAsync(legalEntityId, AttendanceInboxKinds.Correction, corrections.Select(c => c.EmployeeId), ct);
            rows.AddRange(corrections.Where(c => correctionScope.Contains(c.EmployeeId)).Select(CorrectionRow));

            var workAreaScope = await ScopeAsync(legalEntityId, AttendanceInboxKinds.WorkModeChange,
                await _workAreas.ListPendingEmployeeIdsAsync(tenantId, legalEntityId, null, null, ct), ct);
            if (workAreaScope.Count > 0)
                rows.AddRange((await _workAreas.ListApprovalInboxAsync(tenantId, legalEntityId, workAreaScope, null, null, 0, InboxLimit, ct))
                    .Items.Select(WorkAreaRow));

            var locationScope = await ScopeAsync(legalEntityId, AttendanceInboxKinds.LocationChange,
                await _locations.ListPendingEmployeeIdsAsync(tenantId, legalEntityId, ct), ct);
            if (locationScope.Count > 0)
                rows.AddRange((await _locations.ListApprovalInboxAsync(tenantId, legalEntityId, locationScope, 0, InboxLimit, ct))
                    .Items.Select(LocationRow));

            // Devices: requests carrying this legal entity, plus requests with none, scoped through the employee's (D-c).
            var unscoped = await _devices.ListPendingWithoutLegalEntityAsync(tenantId, ct);
            var unscopedOwners = unscoped.Count == 0
                ? new Dictionary<Guid, Employee>()
                : await _employees.ListByIdsAsync(tenantId, unscoped.Select(d => d.EmployeeId).Distinct().ToList(), ct);
            var unscopedHere = unscoped.Where(d => unscopedOwners.GetValueOrDefault(d.EmployeeId)?.LegalEntityId == legalEntityId).ToList();
            var deviceCandidates = (await _devices.ListPendingEmployeeIdsAsync(tenantId, legalEntityId, ct))
                .Concat(unscopedHere.Select(d => d.EmployeeId));
            var deviceScope = await ScopeAsync(legalEntityId, AttendanceInboxKinds.DeviceChange, deviceCandidates, ct);
            if (deviceScope.Count > 0)
            {
                rows.AddRange((await _devices.ListApprovalInboxAsync(tenantId, legalEntityId, deviceScope, 0, InboxLimit, ct)).Items
                    .Concat(unscopedHere.Where(d => deviceScope.Contains(d.EmployeeId)))
                    .Select(d => DeviceRow(d, legalEntityId)));
            }
        }
        return rows;
    }

    private async Task<IReadOnlyCollection<Guid>> ScopeAsync(Guid legalEntityId, string kind, IEnumerable<Guid> candidates, CancellationToken ct)
    {
        var list = candidates.Distinct().ToList();
        if (list.Count == 0)
            return [];
        return await _authority.ResolveApprovalInboxScopeAsync(new EmployeeApprovalInboxScopeRequest(
            legalEntityId, AttendanceInboxKinds.ApprovePermission, AttendanceInboxKinds.Purposes[kind], list), ct);
    }

    private async Task<IReadOnlyList<Guid>> CallerLegalEntitiesAsync(Guid tenantId, CancellationToken ct)
        => (await _employees.GetByUserIdsAsync(tenantId, [_currentUser.UserId], ct))
            .Where(e => e.LegalEntityId is not null).Select(e => e.LegalEntityId!.Value).Distinct().ToList();

    private async Task<List<Row>> SentAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var rows = new List<Row>();
        rows.AddRange((await _corrections.ListMyAsync(tenantId, employeeId, null, null, null, 0, SentLimit, ct)).Items.Select(CorrectionRow));
        rows.AddRange((await _workAreas.ListMyAsync(tenantId, employeeId, null, null, null, 0, SentLimit, ct)).Items.Select(WorkAreaRow));
        rows.AddRange((await _locations.ListMyAsync(tenantId, employeeId, null, 0, SentLimit, ct)).Items.Select(LocationRow));
        rows.AddRange((await _devices.ListForEmployeeAsync(tenantId, employeeId, ct)).Take(SentLimit).Select(d => DeviceRow(d, d.LegalEntityId)));
        return rows;
    }

    private async Task<Row?> LoadRowAsync(Guid tenantId, string kind, Guid id, CancellationToken ct) => kind switch
    {
        AttendanceInboxKinds.Correction => await _corrections.GetByIdAsync(tenantId, id, ct) is { } c ? CorrectionRow(c) : null,
        AttendanceInboxKinds.WorkModeChange => await _workAreas.GetByIdAsync(tenantId, id, ct) is { } w ? WorkAreaRow(w) : null,
        AttendanceInboxKinds.LocationChange => await _locations.GetByIdAsync(tenantId, id, ct) is { } l ? LocationRow(l) : null,
        AttendanceInboxKinds.DeviceChange => await _devices.GetTrackedByIdAsync(tenantId, id, ct) is { } d ? DeviceRow(d, d.LegalEntityId) : null,
        _ => null,
    };

    private static Row CorrectionRow(AttendanceCorrection c) => new(
        AttendanceInboxKinds.Correction, c.Id, c.EmployeeId, c.LegalEntityId, c.Status, c.CreatedAt, c.UpdatedAt,
        $"{Humanize(c.CorrectionType)} · {c.WorkDate:dd MMM yyyy}", c.CorrectionType);

    private static Row WorkAreaRow(WorkAreaChangeRequest w) => new(
        AttendanceInboxKinds.WorkModeChange, w.Id, w.EmployeeId, w.LegalEntityId, w.Status, w.RequestedAt, w.ReviewedAt,
        $"{w.CurrentWorkModeName} → {w.RequestedWorkModeName} · {w.Date:dd MMM yyyy}", null);

    private static Row LocationRow(LocationChangeRequest l) => new(
        AttendanceInboxKinds.LocationChange, l.Id, l.EmployeeId, l.LegalEntityId, l.Status, l.RequestedAt, l.ReviewedAt ?? l.AppliedAt,
        $"New work location · {l.RequestedLatitude.ToString("0.0000", CultureInfo.InvariantCulture)}, {l.RequestedLongitude.ToString("0.0000", CultureInfo.InvariantCulture)}", null);

    private static Row DeviceRow(DeviceChangeRequest d, Guid? legalEntityId) => new(
        AttendanceInboxKinds.DeviceChange, d.Id, d.EmployeeId, legalEntityId, d.Status, d.RequestedAt, d.ReviewedAt,
        $"{d.NewDeviceName} ({d.NewDeviceOs})", null);

    public async Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        var items = await ListAsync(tenantId, new InboxQuery(), callerEmployeeId, ct);
        static InboxFilterOption Option(string value, string label) => new(value, label);
        return
        [
            new("kind", "Kind", true, items.Select(i => i.Kind).Distinct().Select(k => Option(k, AttendanceInboxKinds.Label(k))).ToList()),
            new("correction_type", "Correction type", true, items.Where(i => i.Facets.ContainsKey("correction_type"))
                .Select(i => i.Facets["correction_type"][0]).Distinct().Select(t => Option(t, Humanize(t))).ToList()),
            new("employee", "Employee", true, items.Where(i => i.RequestedBy is not null).Select(i => i.RequestedBy!)
                .DistinctBy(p => p.Id).OrderBy(p => p.Name).Select(p => Option(p.Id.ToString(), p.Name)).ToList()),
            new("signal_key", "Alert", true, items.Where(i => i.Facets.ContainsKey("signal_key"))
                .Select(i => i.Facets["signal_key"][0]).Distinct().Select(s => Option(s, AttendanceAlertInbox.SignalLabel(s))).ToList()),
            new("work_date", "Day", true, items.Where(i => i.Facets.ContainsKey("work_date"))
                .Select(i => i.Facets["work_date"][0]).Distinct().OrderByDescending(d => d, StringComparer.Ordinal).Select(d => Option(d, d)).ToList()),
        ];
    }

    // ---- person case history -------------------------------------------------------------------

    /// <summary>The subject's alerts, or their requests of all four kinds, created in [from, to) and of any status, as
    /// info. CanOpen follows GetDetailAsync: requester, current approver of a pending request, an attendance reader
    /// covering them, or someone added to the chat.</summary>
    public async Task<IReadOnlyList<InboxPersonCase>> ListPersonCasesAsync(
        Guid tenantId, Guid subjectEmployeeId, string category, DateTimeOffset from, DateTimeOffset to, Guid callerEmployeeId, CancellationToken ct)
    {
        var added = (await _conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct)).ToHashSet();
        if (category == InboxCategories.Alert)
            return await _alerts.ListPersonCasesAsync(tenantId, subjectEmployeeId, from, to, callerEmployeeId, added, ct);
        if (category != InboxCategories.Request)
            return [];

        var rows = new List<Row>();
        rows.AddRange((await _corrections.ListForEmployeeCreatedAsync(tenantId, subjectEmployeeId, from, to, ct)).Select(CorrectionRow));
        rows.AddRange((await _workAreas.ListForEmployeeCreatedAsync(tenantId, subjectEmployeeId, from, to, ct)).Select(WorkAreaRow));
        rows.AddRange((await _locations.ListForEmployeeCreatedAsync(tenantId, subjectEmployeeId, from, to, ct)).Select(LocationRow));
        rows.AddRange((await _devices.ListForEmployeeAsync(tenantId, subjectEmployeeId, ct))
            .Where(d => d.RequestedAt >= from && d.RequestedAt < to).Select(d => DeviceRow(d, d.LegalEntityId)));
        if (rows.Count == 0)
            return [];

        var people = await _employees.ListByIdsAsync(tenantId, [subjectEmployeeId], ct);
        var isRequester = subjectEmployeeId == callerEmployeeId;
        // Keyed by legal entity; Guid.Empty stands for "none" (Dictionary keys can't be null).
        var approver = new Dictionary<(string, Guid), bool>();
        var reader = new Dictionary<Guid, bool>();
        var result = new List<InboxPersonCase>();
        foreach (var row in rows)
        {
            var canOpen = isRequester || added.Contains(new ItemRef(Source, row.Kind, row.Id));
            if (!canOpen && IsPending(row.Status))
            {
                if (!approver.TryGetValue((row.Kind, row.LegalEntityId ?? Guid.Empty), out var isApprover))
                    approver[(row.Kind, row.LegalEntityId ?? Guid.Empty)] = isApprover = await IsExactApproverAsync(row, ct);
                canOpen = isApprover;
            }
            if (!canOpen)
            {
                if (!reader.TryGetValue(row.LegalEntityId ?? Guid.Empty, out var covers))
                    reader[row.LegalEntityId ?? Guid.Empty] = covers = await HrReaderCoversAsync(row, ct);
                canOpen = covers;
            }
            result.Add(new InboxPersonCase(Item(row, people, InboxDirections.Info, pending: false, []), canOpen));
        }
        return result;
    }

    // ---- detail ---------------------------------------------------------------------------------

    public async Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
    {
        if (kind == AttendanceInboxKinds.Alert)
        {
            var addedToChat = (await _conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct)).Contains(new ItemRef(Source, kind, id));
            return await _alerts.GetDetailAsync(tenantId, id, callerEmployeeId, addedToChat, ct);
        }
        var row = await LoadRowAsync(tenantId, kind, id, ct);
        var caller = await _employees.GetByIdAsync(tenantId, callerEmployeeId, ct);
        if (row is null || caller is null)
            return null;

        var isRequester = row.EmployeeId == caller.Id;
        var isApprover = IsPending(row.Status) && await IsExactApproverAsync(row, ct);
        var added = !isRequester && !isApprover
            && (await _conversations.ListAddedSubjectsAsync(tenantId, caller.Id, Source, ct)).Contains(new ItemRef(Source, kind, id));
        if (!isRequester && !isApprover && !added && !await HrReaderCoversAsync(row, ct))
            return null;

        var people = await _employees.ListByIdsAsync(tenantId, [row.EmployeeId], ct);
        var actions = isApprover ? ReceivedActions(kind) : isRequester ? SentActions(row) : [];
        var direction = isApprover ? InboxDirections.Received : isRequester ? InboxDirections.Sent : InboxDirections.Info;
        var item = Item(row, people, direction, isApprover || (isRequester && IsPending(row.Status)), actions);
        var (changes, extra, payload) = await ChangeSetAsync(tenantId, kind, id, editable: isApprover, ct);
        return new InboxItemDetail(item, Steps(row, people), row.Subtitle, changes, null, null, null, payload, extra);
    }

    private async Task<bool> HrReaderCoversAsync(Row row, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(AttendanceInboxKinds.ReadPermission))
            return false;
        var legalEntityId = row.LegalEntityId ?? (await _employees.GetByIdAsync(_currentUser.TenantId, row.EmployeeId, ct))?.LegalEntityId;
        return await AttendanceReadCoverage.CoversAsync(_authority, _currentUser.UserId, legalEntityId, row.EmployeeId, ct);
    }

    /// <summary>The same check the module's approve command enforces: the caller is the resolved approver now.</summary>
    private async Task<bool> IsExactApproverAsync(Row row, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(AttendanceInboxKinds.ApprovePermission))
            return false;
        var legalEntityId = row.LegalEntityId ?? (await _employees.GetByIdAsync(_currentUser.TenantId, row.EmployeeId, ct))?.LegalEntityId;
        if (legalEntityId is not Guid le)
            return false;
        var route = await _authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            row.EmployeeId, le, AttendanceInboxKinds.ApprovePermission, AttendanceInboxKinds.Purposes[row.Kind]), ct);
        return route.IsSuccess && route.Value?.ApproverUserId == _currentUser.UserId;
    }

    private async Task<(IReadOnlyList<InboxChange>, string?, string?)> ChangeSetAsync(
        Guid tenantId, string kind, Guid id, bool editable, CancellationToken ct)
    {
        static string? Time(DateTimeOffset? at) => at?.ToString("O");
        switch (kind)
        {
            case AttendanceInboxKinds.Correction:
            {
                var c = (await _corrections.GetByIdAsync(tenantId, id, ct))!;
                var editIn = editable && c.CorrectionType is AttendanceCorrection.TypeClockIn or AttendanceCorrection.TypeFullDay;
                var editOut = editable && c.CorrectionType is AttendanceCorrection.TypeClockOut or AttendanceCorrection.TypeFullDay;
                var editBreaks = editable && c.CorrectionType == AttendanceCorrection.TypeBreak;
                IReadOnlyList<InboxChange> changes =
                [
                    new("correction_type", "Correction", null, Humanize(c.CorrectionType)),
                    new("work_date", "Day", null, c.WorkDate.ToString("yyyy-MM-dd"), "date"),
                    new("clockInAt", "Clock in", Time(c.OriginalClockInAt), Time(c.ApprovedClockInAt ?? c.RequestedClockInAt), "datetime", editIn),
                    new("clockOutAt", "Clock out", Time(c.OriginalClockOutAt), Time(c.ApprovedClockOutAt ?? c.RequestedClockOutAt), "datetime", editOut),
                    new("breaks", "Breaks", c.OriginalBreakJson, c.ApprovedBreakJson ?? c.RequestedBreakJson, "breaks", editBreaks),
                    new("reason", "Reason", null, c.Reason),
                ];
                var payload = JsonSerializer.Serialize(new
                {
                    clockInAt = c.ApprovedClockInAt ?? c.RequestedClockInAt,
                    clockOutAt = c.ApprovedClockOutAt ?? c.RequestedClockOutAt,
                    breaks = string.IsNullOrWhiteSpace(c.ApprovedBreakJson ?? c.RequestedBreakJson)
                        ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>((c.ApprovedBreakJson ?? c.RequestedBreakJson)!),
                }, Web);
                return (changes, JsonSerializer.Serialize(new { c.WorkDate, c.CorrectionType, c.Notes }, Web), payload);
            }
            case AttendanceInboxKinds.WorkModeChange:
            {
                var w = (await _workAreas.GetByIdAsync(tenantId, id, ct))!;
                var options = editable
                    ? (await _workModes.ListByLegalEntityAsync(tenantId, w.LegalEntityId, includeInactive: false, ct))
                        .Select(m => new InboxFilterOption(m.Id.ToString(), m.Name)).ToList()
                    : null;
                IReadOnlyList<InboxChange> changes =
                [
                    new("date", "Day", null, w.Date.ToString("yyyy-MM-dd"), "date"),
                    new("workModeId", "Work mode", w.CurrentWorkModeName, w.RequestedWorkModeId?.ToString(), "select", editable, null,
                        options ?? (w.RequestedWorkModeId is { } requested ? [new InboxFilterOption(requested.ToString(), w.RequestedWorkModeName)] : null)),
                    new("reason", "Reason", null, w.Reason),
                ];
                return (changes, null, JsonSerializer.Serialize(new { workModeId = w.RequestedWorkModeId }, Web));
            }
            case AttendanceInboxKinds.LocationChange:
            {
                var l = (await _locations.GetByIdAsync(tenantId, id, ct))!;
                var current = await _workLocations.GetByEmployeeIdAsync(tenantId, l.EmployeeId, ct);
                var distance = current is null ? (double?)null : DistanceMeters(current.Latitude, current.Longitude, l.RequestedLatitude, l.RequestedLongitude);
                IReadOnlyList<InboxChange> changes =
                [
                    new("location", "Work location",
                        current is null ? null : $"{current.Latitude.ToString(CultureInfo.InvariantCulture)},{current.Longitude.ToString(CultureInfo.InvariantCulture)}",
                        $"{l.RequestedLatitude.ToString(CultureInfo.InvariantCulture)},{l.RequestedLongitude.ToString(CultureInfo.InvariantCulture)}"),
                    new("accuracy", "Accuracy (m)", null, l.RequestedAccuracyMeters?.ToString("0", CultureInfo.InvariantCulture)),
                    new("distance", "Distance from current (m)", null, distance?.ToString("0", CultureInfo.InvariantCulture)),
                    new("reason", "Reason", null, l.Reason),
                ];
                var extra = JsonSerializer.Serialize(new
                {
                    latitude = l.RequestedLatitude, longitude = l.RequestedLongitude, accuracyMeters = l.RequestedAccuracyMeters,
                    currentLatitude = current?.Latitude, currentLongitude = current?.Longitude, distanceMeters = distance, appliedAt = l.AppliedAt,
                }, Web);
                return (changes, extra, null);
            }
            case AttendanceInboxKinds.DeviceChange:
            {
                var d = (await _devices.GetTrackedByIdAsync(tenantId, id, ct))!;
                var old = d.CurrentDeviceRegistrationId is { } oldId ? await _tray.FindDeviceAsync(oldId, tenantId, ct) : null;
                IReadOnlyList<InboxChange> changes =
                [
                    new("device", "Device", old is null ? null : $"{old.DeviceName} ({old.DeviceOs})", $"{d.NewDeviceName} ({d.NewDeviceOs})"),
                    new("fingerprint", "Fingerprint", Short(old?.DeviceFingerprint), Short(d.NewDeviceFingerprint)),
                ];
                return (changes, null, null);
            }
            default:
                return ([], null, null);
        }
    }

    // ---- actions --------------------------------------------------------------------------------

    public async Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
    {
        if (kind == AttendanceInboxKinds.Alert)
            return action == AttendanceInboxActions.Resolve
                ? await _alerts.ResolveAsync(tenantId, id, body, callerEmployeeId, ct)
                : Result<InboxActionResult>.UnprocessableEntity(UnknownAction);
        // The Inbox calls the module commands directly, so it must hold the permission the module endpoints require.
        if (action is AttendanceInboxActions.Approve or AttendanceInboxActions.Reject
            && !_currentUser.HasPermission(AttendanceInboxKinds.ApprovePermission))
            return Result<InboxActionResult>.Forbidden(MissingPermission);
        var comment = BodyString(body, "comment");
        var edited = BodyString(body, "editedPayloadJson");

        Func<CancellationToken, Task<(bool Ok, string? Error, int? Status)>>? run = (kind, action) switch
        {
            (AttendanceInboxKinds.Correction, AttendanceInboxActions.Approve) => async t =>
                Outcome(await _sender.Send(new ApproveAttendanceCorrectionCommand(id, comment, CorrectionEdits(edited)), t)),
            (AttendanceInboxKinds.Correction, AttendanceInboxActions.Reject) => async t =>
                Outcome(await _sender.Send(new RejectAttendanceCorrectionCommand(id, comment), t)),
            (AttendanceInboxKinds.Correction, AttendanceInboxActions.Cancel) => async t =>
                Outcome(await _sender.Send(new CancelAttendanceCorrectionCommand(id), t)),
            (AttendanceInboxKinds.WorkModeChange, AttendanceInboxActions.Approve) => async t =>
                Outcome(await _sender.Send(new ApproveWorkAreaChangeRequestCommand(id, comment, WorkModeEdit(edited)), t)),
            (AttendanceInboxKinds.WorkModeChange, AttendanceInboxActions.Reject) => async t =>
                Outcome(await _sender.Send(new RejectWorkAreaChangeRequestCommand(id, comment ?? string.Empty), t)),
            (AttendanceInboxKinds.WorkModeChange, AttendanceInboxActions.Cancel) => async t =>
                Outcome(await _sender.Send(new CancelWorkAreaChangeRequestCommand(id), t)),
            (AttendanceInboxKinds.LocationChange, AttendanceInboxActions.Approve) => async t =>
                Outcome(await _sender.Send(new ApproveLocationChangeRequestCommand(id, comment), t)),
            (AttendanceInboxKinds.LocationChange, AttendanceInboxActions.Reject) => async t =>
                Outcome(await _sender.Send(new RejectLocationChangeRequestCommand(id, comment), t)),
            (AttendanceInboxKinds.LocationChange, AttendanceInboxActions.Cancel) => async t =>
                Outcome(await _sender.Send(new CancelLocationChangeRequestCommand(id), t)),
            (AttendanceInboxKinds.DeviceChange, AttendanceInboxActions.Approve) => async t =>
                Outcome(await _sender.Send(new ApproveDeviceChangeRequestCommand(id, comment), t)),
            (AttendanceInboxKinds.DeviceChange, AttendanceInboxActions.Reject) => async t =>
                Outcome(await _sender.Send(new RejectDeviceChangeRequestCommand(id, comment), t)),
            _ => null,
        };
        if (run is null)
            return Result<InboxActionResult>.UnprocessableEntity(UnknownAction);

        var before = await SnapshotAsync(tenantId, kind, id, ct);
        if (before is null)
            return Result<InboxActionResult>.NotFound("Attendance request not found.");

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async t =>
            {
                var (ok, error, status) = await run(t);
                if (!ok)
                    throw new ModuleRefused(Result<InboxActionResult>.Failure(error ?? "The action failed.", status ?? 400));
                var after = await SnapshotAsync(tenantId, kind, id, t, includeState: false);
                var now = _clock.UtcNow;
                await _journal.RecordAsync(new InboxDecisionJournalEntry
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Source = Source, Kind = kind, ItemId = id, Action = action,
                    ActorEmployeeId = callerEmployeeId, BeforeJson = before.ToJson(), AfterJson = after?.ToJson(),
                    Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), DecidedAt = now, CreatedAt = now,
                }, t);
                var row = await LoadRowAsync(tenantId, kind, id, t);
                return Result<InboxActionResult>.Success(new InboxActionResult(row is null ? [callerEmployeeId] : [row.EmployeeId, callerEmployeeId]));
            }, ct);
        }
        catch (ModuleRefused refused)
        {
            _unitOfWork.ClearTracking();
            return refused.Result;
        }
    }

    private sealed class ModuleRefused(Result<InboxActionResult> result) : Exception(result.Error)
    {
        public Result<InboxActionResult> Result { get; } = result;
    }

    private static (bool, string?, int?) Outcome<T>(Result<T> result) => (result.IsSuccess, result.Error, result.StatusCode);

    /// <summary>The before-state an undo needs: for a correction the attendance day (and its breaks for a break
    /// correction); for a work mode the requested mode and the day's planned mode; for a location the registered work
    /// location; for a device the old registration.</summary>
    public async Task<AttendanceDecisionSnapshot?> SnapshotAsync(
        Guid tenantId, string kind, Guid id, CancellationToken ct, bool includeState = true)
    {
        var row = await LoadRowAsync(tenantId, kind, id, ct);
        if (row is null)
            return null;
        var title = $"{AttendanceInboxKinds.Label(kind)} - {row.Subtitle}";
        if (!includeState)
            return new AttendanceDecisionSnapshot(title, row.Status);

        switch (kind)
        {
            case AttendanceInboxKinds.Correction:
            {
                var c = (await _corrections.GetByIdAsync(tenantId, id, ct))!;
                var record = await _attendance.GetRecordAsync(tenantId, c.EmployeeId, c.WorkDate, ct);
                IReadOnlyList<AttendanceBreakSnapshot> breaks = [];
                if (record is not null && c.CorrectionType == AttendanceCorrection.TypeBreak)
                {
                    var (from, to) = await DayWindowAsync(tenantId, c.LegalEntityId, c.WorkDate, ct);
                    breaks = (await _attendance.ListBreaksAsync(tenantId, c.EmployeeId, from, to, ct))
                        .Select(b => new AttendanceBreakSnapshot(b.BreakStart, b.BreakEnd, b.BreakType)).ToList();
                }
                return new AttendanceDecisionSnapshot(title, row.Status, record is null ? null : new AttendanceRecordSnapshot(
                    record.ActualStart, record.ActualEnd, record.BreakMinutes, record.WorkedMinutes, record.LateMinutes, record.Status, breaks));
            }
            case AttendanceInboxKinds.WorkModeChange:
            {
                var w = (await _workAreas.GetByIdAsync(tenantId, id, ct))!;
                var record = await _attendance.GetRecordAsync(tenantId, w.EmployeeId, w.Date, ct);
                return new AttendanceDecisionSnapshot(title, row.Status, WorkModeId: w.RequestedWorkModeId, WorkModeName: w.RequestedWorkModeName,
                    RecordWorkModeId: record?.ExpectedWorkModeId, RecordWorkModeName: record?.ExpectedWorkModeName);
            }
            case AttendanceInboxKinds.LocationChange:
            {
                var l = (await _locations.GetByIdAsync(tenantId, id, ct))!;
                var current = await _workLocations.GetByEmployeeIdAsync(tenantId, l.EmployeeId, ct);
                return new AttendanceDecisionSnapshot(title, row.Status, WorkLocation: current is null
                    ? null : new WorkLocationSnapshot(current.Latitude, current.Longitude, current.AccuracyMeters, current.UpdatedAt));
            }
            case AttendanceInboxKinds.DeviceChange:
            {
                var d = (await _devices.GetTrackedByIdAsync(tenantId, id, ct))!;
                var old = d.CurrentDeviceRegistrationId is { } oldId ? await _tray.FindDeviceAsync(oldId, tenantId, ct) : null;
                return new AttendanceDecisionSnapshot(title, row.Status, OldDeviceRegistrationId: d.CurrentDeviceRegistrationId, OldDeviceActive: old?.IsActive);
            }
            default:
                return null;
        }
    }

    /// <summary>The work day in the company's own time zone, as UTC bounds (the same window the correction workflow uses).</summary>
    public async Task<(DateTimeOffset From, DateTimeOffset To)> DayWindowAsync(Guid tenantId, Guid legalEntityId, DateOnly date, CancellationToken ct)
    {
        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, legalEntityId, ct);
        var zone = legalEntity is null ? TimeZoneInfo.Utc : AttendanceScheduleResolver.ResolveForDate(legalEntity, date, _clock.UtcNow).TimeZone;
        var localMidnight = date.ToDateTime(TimeOnly.MinValue);
        var from = new DateTimeOffset(localMidnight, zone.GetUtcOffset(localMidnight));
        return (from, from.AddDays(1));
    }

    private static AttendanceCorrectionEditedValues? CorrectionEdits(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var edit = JsonSerializer.Deserialize<CorrectionEditPayload>(json, Web);
            return edit is null ? null : new AttendanceCorrectionEditedValues(edit.ClockInAt, edit.ClockOutAt,
                edit.Breaks?.Select(b => new AttendanceCorrectionInputBreak(b.BreakStart, b.BreakEnd, b.BreakType)).ToList());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid? WorkModeEdit(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("workModeId", out var value) && value.TryGetGuid(out var id) ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CorrectionEditPayload(DateTimeOffset? ClockInAt, DateTimeOffset? ClockOutAt, IReadOnlyList<BreakPayload>? Breaks);
    private sealed record BreakPayload(DateTimeOffset BreakStart, DateTimeOffset BreakEnd, string BreakType);

    // ---- mapping --------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static bool IsPending(string status) => status == "pending";

    private static List<InboxAction> ReceivedActions(string kind) =>
    [
        new(AttendanceInboxActions.Approve, "Approve", "primary", NeedsComment: false, NeedsPayload: false),
        new(AttendanceInboxActions.Reject, "Reject", "danger", NeedsComment: kind != AttendanceInboxKinds.Correction, NeedsPayload: false),
    ];

    private static List<InboxAction> SentActions(Row row)
        => IsPending(row.Status) && row.Kind != AttendanceInboxKinds.DeviceChange
            ? [new(AttendanceInboxActions.Cancel, "Cancel request", "danger", NeedsComment: false, NeedsPayload: false)]
            : [];

    private static InboxItem Item(Row row, IReadOnlyDictionary<Guid, Employee> people, string direction, bool pending, IReadOnlyList<InboxAction> actions)
    {
        var employee = people.GetValueOrDefault(row.EmployeeId);
        var name = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();
        var facets = new Dictionary<string, IReadOnlyList<string>>
        {
            ["employee"] = [row.EmployeeId.ToString()],
            ["kind"] = [row.Kind],
        };
        if (employee?.DepartmentId is { } department)
            facets["department"] = [department.ToString()];
        if (row.CorrectionType is { } type)
            facets["correction_type"] = [type];
        return new InboxItem(
            AttendanceInboxKinds.Source, row.Kind, row.Id, InboxCategories.Request, row.CorrectionType ?? row.Kind,
            $"{AttendanceInboxKinds.Label(row.Kind)} - {name}", row.Subtitle, row.Status, direction, null,
            "legal_entity", row.LegalEntityId ?? employee?.LegalEntityId, null, new InboxPerson(row.EmployeeId, name), null,
            row.CreatedAt, row.UpdatedAt ?? row.CreatedAt, actions, 0, pending, facets);
    }

    private static IReadOnlyList<InboxStep> Steps(Row row, IReadOnlyDictionary<Guid, Employee> people)
    {
        var employee = people.GetValueOrDefault(row.EmployeeId);
        var steps = new List<InboxStep>
        {
            new("submitted", "Requested", new InboxPerson(row.EmployeeId, employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim()),
                row.CreatedAt, InboxStepStates.Done, null),
        };
        steps.Add(row.Status switch
        {
            "pending" => new InboxStep("decision", "Waiting for approval", null, null, InboxStepStates.Current, null),
            "applied" => new InboxStep("decision", "Approved and applied", null, row.UpdatedAt, InboxStepStates.Done, null),
            var status => new InboxStep("decision", Humanize(status), null, row.UpdatedAt, InboxStepStates.Done, null),
        });
        return steps;
    }

    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earth = 6_371_000d;
        double Rad(double degrees) => degrees * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earth * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string? Short(string? fingerprint)
        => string.IsNullOrEmpty(fingerprint) ? null : fingerprint.Length <= 12 ? fingerprint : fingerprint[..12] + "…";

    private static string Humanize(string value)
    {
        var text = value.Replace('_', ' ');
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string? BodyString(JsonElement? body, string property)
        => body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
