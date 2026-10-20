using System.Globalization;
using ONEVO.Application.Features.Conversations.Contracts;
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using Employee = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Application.Features.TimeAttendance.Inbox;

/// <summary>
/// Attendance alerts in the Inbox (plan E2), kind "alert", category alert. Received = open alerts of employees the
/// caller may review right now (attendance:approve, the attendance approver route - ResolveApprovalInboxScopeAsync
/// batched once per legal entity, the same check the resolve action makes). The employee sees their own alerts as
/// info. Resolve sets an outcome: excused and confirmed only mark the alert; convert_to_leave (absences only) runs the
/// Leave module's CreateApproverBackdatedLeaveCommand and links the leave. Every resolution is journaled with the
/// before/after state the 30-minute undo needs.
/// </summary>
public sealed class AttendanceAlertInbox
{
    public const int ReceivedLimit = 500;
    public const string AlreadyResolved = "This alert was already resolved.";
    public const string BadOutcome = "Choose excused, confirmed or convert to leave.";
    public const string ConvertOnlyAbsence = "Only an absence can be turned into leave.";
    public const string LeaveTypeRequired = "Choose the leave type for the absence.";

    private static readonly IReadOnlyDictionary<string, string> SignalLabels = new Dictionary<string, string>
    {
        [AttendanceAlertSignals.AbsentDays] = "Absent",
        [AttendanceAlertSignals.LateClockIns] = "Late clock-in",
        [AttendanceAlertSignals.EarlyClockOuts] = "Early clock-out",
        [AttendanceAlertSignals.ShortHoursDays] = "Short hours",
        [AttendanceAlertSignals.OffScheduleWork] = "Worked off schedule",
        [AttendanceAlertSignals.LocationViolations] = "Outside work location",
        [AttendanceAlertSignals.IdleActivityAlerts] = "Idle or low activity",
        [AttendanceAlertSignals.MissingClockOuts] = "Missing clock-out",
    };

    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeAuthorityResolver _authority;
    private readonly IAttendanceAlertRepository _alerts;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeaveApprovalRepository _leaveApprovals;
    private readonly ISender _sender;
    private readonly IInboxDecisionJournal _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AttendanceAlertInbox(
        ICurrentUser currentUser, IEmployeeRepository employees, IEmployeeAuthorityResolver authority, IAttendanceAlertRepository alerts,
        ILeaveEntitlementRepository entitlements, ILeaveApprovalRepository leaveApprovals, ISender sender, IInboxDecisionJournal journal,
        IUnitOfWork unitOfWork, IDateTimeProvider clock,
        AttendanceDeductionService? deductions = null, IAttendanceAlertDeductionRepository? deductionRows = null)
    {
        _deductions = deductions;
        _deductionRows = deductionRows;
        _currentUser = currentUser;
        _employees = employees;
        _authority = authority;
        _alerts = alerts;
        _entitlements = entitlements;
        _leaveApprovals = leaveApprovals;
        _sender = sender;
        _journal = journal;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    private readonly AttendanceDeductionService? _deductions;
    private readonly IAttendanceAlertDeductionRepository? _deductionRows;

    public static string SignalLabel(string signal) => SignalLabels.GetValueOrDefault(signal, "Attendance alert");

    // ---- list -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<InboxItem>> ListAsync(
        Guid tenantId, Guid callerEmployeeId, IReadOnlyList<Guid> callerLegalEntities, CancellationToken ct)
    {
        var received = new List<AttendanceAlert>();
        if (_currentUser.HasPermission(AttendanceInboxKinds.ApprovePermission))
        {
            foreach (var legalEntityId in callerLegalEntities)
            {
                var candidates = await _alerts.ListOpenEmployeeIdsAsync(tenantId, legalEntityId, ct);
                var scope = (await ScopeAsync(legalEntityId, candidates, ct)).Where(e => e != callerEmployeeId).ToList();
                if (scope.Count > 0)
                    received.AddRange(await _alerts.ListOpenAsync(tenantId, legalEntityId, scope, ReceivedLimit, ct));
            }
        }
        var own = await _alerts.ListForEmployeeAsync(
            tenantId, callerEmployeeId, _clock.Today.AddDays(-AttendanceAlertMaterializer.WindowDays), _clock.Today, ct);

        var ids = received.Concat(own).Select(a => a.EmployeeId).Distinct().ToList();
        var people = ids.Count == 0 ? new Dictionary<Guid, Employee>() : await _employees.ListByIdsAsync(tenantId, ids, ct);
        var items = new Dictionary<Guid, InboxItem>();
        foreach (var alert in received)
            items.TryAdd(alert.Id, Item(alert, people, InboxDirections.Received, pending: true, [ResolveAction(alert)]));
        foreach (var alert in own)
            items.TryAdd(alert.Id, Item(alert, people, InboxDirections.Info, pending: false, []));
        return items.Values.ToList();
    }

    /// <summary>The subject's alerts of any status whose work day falls in [from, to), as info: an alert is about its work
    /// day, not the (often batched) detection run, so the row is dated - and bucketed - at <see cref="WorkDayAnchor"/>. CanOpen follows GetDetailAsync: the
    /// subject, their reviewer, an attendance reader covering them, or someone added to the alert's chat.</summary>
    public async Task<IReadOnlyList<InboxPersonCase>> ListPersonCasesAsync(
        Guid tenantId, Guid subjectEmployeeId, DateTimeOffset from, DateTimeOffset to, Guid callerEmployeeId,
        IReadOnlySet<ItemRef> addedToChat, CancellationToken ct)
    {
        // A day either side, then the exact cut on the anchor instant.
        var alerts = (await _alerts.ListForEmployeeAsync(tenantId, subjectEmployeeId,
                DateOnly.FromDateTime(from.UtcDateTime).AddDays(-1), DateOnly.FromDateTime(to.UtcDateTime).AddDays(1), ct))
            .Where(a => WorkDayAnchor(a.WorkDate) >= from && WorkDayAnchor(a.WorkDate) < to).ToList();
        if (alerts.Count == 0)
            return [];
        var people = await _employees.ListByIdsAsync(tenantId, [subjectEmployeeId], ct);
        var isSubject = subjectEmployeeId == callerEmployeeId;
        // Reviewer / reader access depends only on the employee and legal entity, so one check per legal entity.
        // Keyed by legal entity; Guid.Empty stands for "none" (Dictionary keys can't be null).
        var access = new Dictionary<Guid, bool>();
        var rows = new List<InboxPersonCase>();
        foreach (var alert in alerts)
        {
            var canOpen = isSubject || addedToChat.Contains(new ItemRef(AttendanceInboxKinds.Source, AttendanceInboxKinds.Alert, alert.Id));
            if (!canOpen)
            {
                var key = alert.LegalEntityId ?? Guid.Empty;
                if (!access.TryGetValue(key, out var allowed))
                    access[key] = allowed = await CanReviewAsync(alert, callerEmployeeId, ct) || await HrReaderCoversAsync(alert, ct);
                canOpen = allowed;
            }
            var item = Item(alert, people, InboxDirections.Info, pending: alert.Status == AttendanceAlertStatuses.Open, []);
            rows.Add(new InboxPersonCase(item, canOpen));
        }
        return rows;
    }

    /// <summary>Noon UTC of the work day: the same calendar day in every viewer offset from -12h to +12h.</summary>
    public static DateTimeOffset WorkDayAnchor(DateOnly workDate) => new(workDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private async Task<IReadOnlyCollection<Guid>> ScopeAsync(Guid legalEntityId, IEnumerable<Guid> candidates, CancellationToken ct)
    {
        var list = candidates.Distinct().ToList();
        if (list.Count == 0)
            return [];
        return await _authority.ResolveApprovalInboxScopeAsync(new EmployeeApprovalInboxScopeRequest(
            legalEntityId, AttendanceInboxKinds.ApprovePermission, AttendanceInboxKinds.Purposes[AttendanceInboxKinds.Alert], list), ct);
    }

    /// <summary>The check the resolve action makes: attendance:approve and the employee in the caller's approver scope.</summary>
    public async Task<bool> CanReviewAsync(AttendanceAlert alert, Guid callerEmployeeId, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(AttendanceInboxKinds.ApprovePermission) || alert.EmployeeId == callerEmployeeId)
            return false;
        var legalEntityId = alert.LegalEntityId ?? (await _employees.GetByIdAsync(_currentUser.TenantId, alert.EmployeeId, ct))?.LegalEntityId;
        return legalEntityId is Guid le && (await ScopeAsync(le, [alert.EmployeeId], ct)).Contains(alert.EmployeeId);
    }

    private async Task<bool> HrReaderCoversAsync(AttendanceAlert alert, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(AttendanceInboxKinds.ReadPermission))
            return false;
        var legalEntityId = alert.LegalEntityId ?? (await _employees.GetByIdAsync(_currentUser.TenantId, alert.EmployeeId, ct))?.LegalEntityId;
        return await AttendanceReadCoverage.CoversAsync(_authority, _currentUser.UserId, legalEntityId, alert.EmployeeId, ct);
    }

    // ---- detail ---------------------------------------------------------------------------------

    public async Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, Guid id, Guid callerEmployeeId, bool addedToChat, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(tenantId, id, ct);
        if (alert is null)
            return null;
        var isSubject = alert.EmployeeId == callerEmployeeId;
        var reviewer = await CanReviewAsync(alert, callerEmployeeId, ct);
        if (!isSubject && !reviewer && !addedToChat && !await HrReaderCoversAsync(alert, ct))
            return null;

        var people = await _employees.ListByIdsAsync(tenantId, [alert.EmployeeId], ct);
        var open = alert.Status == AttendanceAlertStatuses.Open;
        var actionable = reviewer && open;
        var item = Item(alert, people, reviewer ? InboxDirections.Received : InboxDirections.Info, actionable,
            actionable ? [ResolveAction(alert)] : []);

        var isAbsence = alert.SignalKey == AttendanceAlertSignals.AbsentDays;
        var deduction = _deductionRows is null ? null : (await _deductionRows.ListByAlertsAsync(tenantId, [alert.Id], ct)).GetValueOrDefault(alert.Id);
        var charged = deduction?.Status == AttendanceAlertDeductionStatuses.Charged;
        IReadOnlyList<InboxFilterOption> outcomes =
        [
            new(AttendanceAlertResolveOutcomes.Excused, charged ? "Excused - stop counting it and refund the deduction" : "Excused - stop counting it"),
            new(AttendanceAlertResolveOutcomes.Confirmed, "Confirmed - keep counting it"),
            .. isAbsence ? [new InboxFilterOption(AttendanceAlertResolveOutcomes.ConvertToLeave, "Convert to leave")] : Array.Empty<InboxFilterOption>(),
        ];
        var changes = new List<InboxChange>
        {
            new("signal", "Alert", null, SignalLabel(alert.SignalKey)),
            new("work_date", "Day", null, alert.WorkDate.ToString("yyyy-MM-dd"), "date"),
            new("outcome", "Outcome", null, alert.Outcome ?? AttendanceAlertResolveOutcomes.Confirmed, "select", actionable, null, outcomes),
        };
        if (isAbsence && actionable)
            changes.Add(new("leaveTypeId", "Leave type (convert to leave)", null, null, "select", true, null,
                await LeaveTypeOptionsAsync(tenantId, alert, ct)));
        if (alert.Note is { } note)
            changes.Add(new("note", "Note", null, note));

        var extra = JsonSerializer.Serialize(new
        {
            alert.SignalKey, alert.WorkDate, alert.Severity, alert.Status, alert.Outcome, alert.ConvertedLeaveRequestId,
            details = string.IsNullOrWhiteSpace(alert.DetailsJson) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(alert.DetailsJson),
        }, Web);
        var payload = actionable ? JsonSerializer.Serialize(new { outcome = AttendanceAlertResolveOutcomes.Confirmed }, Web) : null;
        return new InboxItemDetail(item, Steps(alert, people), $"{SignalLabel(alert.SignalKey)} · {alert.WorkDate:dd MMM yyyy}",
            changes, null, null, null, payload, extra, await DeductionFactsAsync(tenantId, alert, deduction, ct));
    }

    /// <summary>What a late/early alert cost under the clock-in policy: the minutes and the leave charged (or that the
    /// charge is pending / was refunded). Null when the day was never charged.</summary>
    private async Task<IReadOnlyList<InboxFact>?> DeductionFactsAsync(
        Guid tenantId, AttendanceAlert alert, AttendanceAlertDeduction? deduction, CancellationToken ct)
    {
        if (deduction is null)
            return null;
        var type = (await _leaveApprovals.ListLeaveTypeNamesAsync(tenantId, [deduction.LeaveTypeId], ct)).GetValueOrDefault(deduction.LeaveTypeId, "leave");
        var amount = string.Create(CultureInfo.InvariantCulture, $"{deduction.Hours:0.##} h from {type}");
        return
        [
            new(alert.SignalKey == AttendanceAlertSignals.EarlyClockOuts ? "Left early by" : "Late by", $"{deduction.Minutes} min"),
            deduction.Status switch
            {
                AttendanceAlertDeductionStatuses.Charged => new InboxFact("Deducted", amount),
                AttendanceAlertDeductionStatuses.Pending => new InboxFact("Deduction pending", amount),
                _ => new InboxFact("Refunded", amount),
            },
        ];
    }

    /// <summary>The leave types the employee holds an entitlement for in that year; the leave command still runs every
    /// eligibility rule when the absence is converted.</summary>
    private async Task<IReadOnlyList<InboxFilterOption>> LeaveTypeOptionsAsync(Guid tenantId, AttendanceAlert alert, CancellationToken ct)
    {
        var typeIds = (await _entitlements.ListExistingAsync(tenantId, alert.WorkDate.Year, [alert.EmployeeId], ct))
            .Select(e => e.LeaveTypeId).Distinct().ToList();
        if (typeIds.Count == 0)
            return [];
        var names = await _leaveApprovals.ListLeaveTypeNamesAsync(tenantId, typeIds, ct);
        return typeIds.Select(t => new InboxFilterOption(t.ToString(), names.GetValueOrDefault(t, "Leave"))).OrderBy(o => o.Label).ToList();
    }

    // ---- resolve --------------------------------------------------------------------------------

    public async Task<Result<InboxActionResult>> ResolveAsync(
        Guid tenantId, Guid id, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(AttendanceInboxKinds.ApprovePermission))
            return Result<InboxActionResult>.Forbidden(AttendanceInboxSourceProvider.MissingPermission);
        var outcome = Field(body, "outcome");
        if (!AttendanceAlertResolveOutcomes.IsValid(outcome))
            return Result<InboxActionResult>.UnprocessableEntity(BadOutcome);
        var note = Field(body, "comment");
        var alert = await _alerts.GetByIdAsync(tenantId, id, ct);
        if (alert is null)
            return Result<InboxActionResult>.NotFound("Attendance alert not found.");
        if (alert.Status != AttendanceAlertStatuses.Open)
            return Result<InboxActionResult>.Conflict(AlreadyResolved);
        if (!await CanReviewAsync(alert, callerEmployeeId, ct))
            return Result<InboxActionResult>.Forbidden(AttendanceInboxSourceProvider.MissingPermission);
        Guid? leaveTypeId = null;
        if (outcome == AttendanceAlertResolveOutcomes.ConvertToLeave)
        {
            if (alert.SignalKey != AttendanceAlertSignals.AbsentDays)
                return Result<InboxActionResult>.UnprocessableEntity(ConvertOnlyAbsence);
            if (!Guid.TryParse(Field(body, "leaveTypeId"), out var typeId))
                return Result<InboxActionResult>.UnprocessableEntity(LeaveTypeRequired);
            leaveTypeId = typeId;
        }

        var before = Snapshot(alert);
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async t =>
            {
                var stored = outcome!;
                if (leaveTypeId is Guid type)
                {
                    var leave = await _sender.Send(new CreateApproverBackdatedLeaveCommand(alert.EmployeeId, type, alert.WorkDate,
                        string.IsNullOrWhiteSpace(note) ? null : note.Trim()), t);
                    if (!leave.IsSuccess)
                        throw new Refused(Result<InboxActionResult>.Failure(leave.Error ?? "The leave could not be created.", leave.StatusCode ?? 422));
                    alert.ConvertedLeaveRequestId = leave.Value;
                    stored = AttendanceAlertOutcomes.ConvertedToLeave;
                }
                var now = _clock.UtcNow;
                alert.Status = AttendanceAlertStatuses.Resolved;
                alert.Outcome = stored;
                alert.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
                alert.ResolvedByEmployeeId = callerEmployeeId;
                alert.ResolvedAt = now;
                alert.UpdatedAt = now;
                // Excusing (or converting) stops the day counting, so its late/early deduction is refunded now.
                if (_deductions is not null && AttendanceAlertOutcomes.StopsCounting(stored))
                    await _deductions.ReconcileAsync(tenantId, alert, null, callerEmployeeId, t);
                var leaveUpdatedAt = alert.ConvertedLeaveRequestId is Guid leaveId
                    ? (await _leaveApprovals.GetStateAsync(tenantId, leaveId, t))?.Request.UpdatedAt
                    : null;
                await _journal.RecordAsync(new InboxDecisionJournalEntry
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Source = AttendanceInboxKinds.Source, Kind = AttendanceInboxKinds.Alert,
                    ItemId = alert.Id, Action = InboxJournalActions.Resolve(stored), ActorEmployeeId = callerEmployeeId,
                    BeforeJson = before.ToJson(), AfterJson = (Snapshot(alert) with { LeaveUpdatedAt = leaveUpdatedAt }).ToJson(),
                    Comment = alert.Note, DecidedAt = now, CreatedAt = now,
                }, t);
                await _alerts.SaveChangesAsync(t);
                return Result<InboxActionResult>.Success(new InboxActionResult([alert.EmployeeId, callerEmployeeId]));
            }, ct);
        }
        catch (Refused refused)
        {
            _unitOfWork.ClearTracking();
            return refused.Result;
        }
    }

    private sealed class Refused(Result<InboxActionResult> result) : Exception(result.Error)
    {
        public Result<InboxActionResult> Result { get; } = result;
    }

    public static AttendanceAlertSnapshot Snapshot(AttendanceAlert a) => new(
        $"{SignalLabel(a.SignalKey)} · {a.WorkDate:dd MMM yyyy}", a.Status, a.Outcome, a.Note, a.ResolvedByEmployeeId, a.ResolvedAt,
        a.ConvertedLeaveRequestId);

    // ---- mapping --------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static InboxAction ResolveAction(AttendanceAlert alert) => new(
        AttendanceInboxActions.Resolve, "Resolve", "primary", NeedsComment: false, NeedsPayload: true,
        new Dictionary<string, string>
        {
            ["outcomes"] = alert.SignalKey == AttendanceAlertSignals.AbsentDays
                ? $"{AttendanceAlertResolveOutcomes.Excused},{AttendanceAlertResolveOutcomes.Confirmed},{AttendanceAlertResolveOutcomes.ConvertToLeave}"
                : $"{AttendanceAlertResolveOutcomes.Excused},{AttendanceAlertResolveOutcomes.Confirmed}",
        });

    private static InboxItem Item(
        AttendanceAlert alert, IReadOnlyDictionary<Guid, Employee> people, string direction, bool pending, IReadOnlyList<InboxAction> actions)
    {
        var employee = people.GetValueOrDefault(alert.EmployeeId);
        var name = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();
        var facets = new Dictionary<string, IReadOnlyList<string>>
        {
            ["employee"] = [alert.EmployeeId.ToString()],
            ["kind"] = [AttendanceInboxKinds.Alert],
            ["signal_key"] = [alert.SignalKey],
            ["work_date"] = [alert.WorkDate.ToString("yyyy-MM-dd")],
        };
        if (employee?.DepartmentId is { } department)
            facets["department"] = [department.ToString()];
        return new InboxItem(
            AttendanceInboxKinds.Source, AttendanceInboxKinds.Alert, alert.Id, InboxCategories.Alert, alert.SignalKey,
            $"{SignalLabel(alert.SignalKey)} - {name}", alert.WorkDate.ToString("dd MMM yyyy"), alert.Outcome ?? alert.Status, direction,
            alert.Severity, "legal_entity", alert.LegalEntityId ?? employee?.LegalEntityId, null, new InboxPerson(alert.EmployeeId, name), null,
            // Dated by the work day it is about, not the (often batched) detection run; the Detected step keeps that.
            WorkDayAnchor(alert.WorkDate), alert.ResolvedAt ?? WorkDayAnchor(alert.WorkDate), actions, 0, pending, facets);
    }

    private static IReadOnlyList<InboxStep> Steps(AttendanceAlert alert, IReadOnlyDictionary<Guid, Employee> people)
    {
        var steps = new List<InboxStep>
        {
            new("detected", "Detected", null, alert.DetectedAt, InboxStepStates.Done, null),
        };
        steps.Add(alert.Status == AttendanceAlertStatuses.Open
            ? new InboxStep("review", "Waiting for review", null, null, InboxStepStates.Current, null)
            : new InboxStep("review", $"Resolved - {Humanize(alert.Outcome ?? AttendanceAlertOutcomes.Confirmed)}",
                alert.ResolvedByEmployeeId is Guid by ? new InboxPerson(by, people.TryGetValue(by, out var p) ? $"{p.FirstName} {p.LastName}".Trim() : "Reviewer") : null,
                alert.ResolvedAt, InboxStepStates.Done, alert.Note));
        return steps;
    }

    private static string Humanize(string value)
    {
        var text = value.Replace('_', ' ');
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>A body field, top-level or inside editedPayloadJson (the detail's editable payload).</summary>
    private static string? Field(JsonElement? body, string property)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b)
            return null;
        if (b.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        if (b.TryGetProperty("editedPayloadJson", out var edited) && edited.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var doc = JsonDocument.Parse(edited.GetString()!);
                return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(property, out var inner)
                    && inner.ValueKind == JsonValueKind.String ? inner.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return null;
    }
}
