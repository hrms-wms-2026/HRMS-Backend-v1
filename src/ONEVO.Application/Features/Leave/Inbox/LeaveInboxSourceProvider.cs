using System.Globalization;
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;
using ONEVO.Application.Features.Leave.Cancellation.Commands;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Inbox;

/// <summary>
/// Leave requests in the Inbox (plan L1). Received = requests the caller can act on now (the same query as the
/// pending-approvals screen and the My Team source, incl. in_order gating and delegation); decided-by-me = the
/// approval history; sent = own requests and those submitted on someone's behalf; plus requests whose chat the caller
/// was added to (read-only). Every action runs the Leave module's own commands - the Inbox adds the journal row (same
/// transaction) and, for request/respond info, the tagged chat message (plan D-d).
/// </summary>
public sealed class LeaveInboxSourceProvider : IInboxSourceProvider, IInboxBulkActionProvider, IInboxPersonCaseProvider
{
    public const int DecidedHistoryDays = 90;
    public const int DecidedHistoryLimit = 500;
    public const string LeaveReadPermission = "leave:read";
    public const string LeaveApprovePermission = "leave:approve";
    public const string LeaveManagePermission = "leave:manage";
    public const string LeaveReadOwnPermission = "leave:read-own";
    public const string MissingPermission = "You do not have permission to do this.";
    private const string UnknownAction = "This action is not available for this item.";

    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveApprovalRepository _approvals;
    private readonly ILeaveRequestRepository _requests;
    private readonly ISender _sender;
    private readonly IInboxDecisionJournal _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationRepository _conversations;
    private readonly IDateTimeProvider _clock;

    public LeaveInboxSourceProvider(
        ICurrentUser currentUser, IEmployeeRepository employees, ILeaveApprovalRepository approvals, ILeaveRequestRepository requests,
        ISender sender, IInboxDecisionJournal journal, IUnitOfWork unitOfWork,
        IConversationRepository conversations, IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _employees = employees;
        _approvals = approvals;
        _requests = requests;
        _sender = sender;
        _journal = journal;
        _unitOfWork = unitOfWork;
        _conversations = conversations;
        _clock = clock;
    }

    public string Source => LeaveInboxKinds.Source;
    public string Label => "Leave";

    // ---- list -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct)
    {
        if (query.Category is { Count: > 0 } categories && !categories.Contains(InboxCategories.Request))
            return [];
        var caller = await _employees.GetByIdAsync(tenantId, callerEmployeeId, ct);
        if (caller is null)
            return [];

        var items = new Dictionary<Guid, InboxItem>();
        // Pending for the caller (scope = act: only with the permission the approve endpoint requires): the shared
        // actionable query (it loads each pending request's state for the mode check).
        var approver = _currentUser.HasPermission(LeaveApprovePermission);
        if (approver)
        {
            foreach (var (_, state) in await LeavePendingApprovals.ListActionableAsync(
                         _approvals, tenantId, caller.Id, new LeaveApprovalListFilter(null, null, null, null, null), ct))
                items.TryAdd(state.Request.Id, Item(ListRow.Of(state), InboxDirections.Received, pending: true, ReceivedActions()));
        }

        // Decided by me, own requests, on-behalf requests: mapped from their rows plus one batched employee lookup
        // (no per-item state loads in the list path).
        var since = DateOnly.FromDateTime(_clock.UtcNow.AddDays(-DecidedHistoryDays).UtcDateTime);
        var history = approver
            ? await _approvals.ListApprovalHistoryAsync(tenantId, caller.Id, since, null, DecidedHistoryLimit, ct)
            : [];
        var onBehalf = await _approvals.ListSubmittedOnBehalfAsync(tenantId, caller.UserId, ct);
        var employeeIds = history.Select(h => h.Request.EmployeeId).Concat(onBehalf.Select(r => r.EmployeeId)).Distinct().ToList();
        var employees = employeeIds.Count == 0
            ? new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            : await _employees.ListByIdsAsync(tenantId, employeeIds, ct);
        foreach (var row in history)
            items.TryAdd(row.Request.Id, Item(ListRow.Of(row.Request, employees.GetValueOrDefault(row.Request.EmployeeId), row.EmployeeName, row.LeaveTypeName),
                InboxDirections.Received, pending: false, []));

        foreach (var row in await _requests.ListOwnAsync(tenantId, caller.Id, new LeaveRequestListFilter(null, null, null, null), ct))
            items.TryAdd(row.Request.Id, Item(ListRow.Of(row.Request, caller, null, row.LeaveTypeName),
                InboxDirections.Sent, IsOpen(row.Request), SentActions(row.Request, caller.Id)));
        if (onBehalf.Count > 0)
        {
            var typeNames = await _approvals.ListLeaveTypeNamesAsync(tenantId, onBehalf.Select(r => r.LeaveTypeId).Distinct().ToList(), ct);
            foreach (var request in onBehalf)
                items.TryAdd(request.Id, Item(ListRow.Of(request, employees.GetValueOrDefault(request.EmployeeId), null,
                    typeNames.GetValueOrDefault(request.LeaveTypeId, "Leave")), InboxDirections.Sent, IsOpen(request), SentActions(request, caller.Id)));
        }

        foreach (var subject in await _conversations.ListAddedSubjectsAsync(tenantId, caller.Id, Source, ct))
        {
            if (items.ContainsKey(subject.Id))
                continue;
            var state = await _approvals.GetStateAsync(tenantId, subject.Id, ct);
            if (state is not null)
                items.TryAdd(subject.Id, Item(ListRow.Of(state), InboxDirections.Info, pending: false, []));
        }
        return items.Values.ToList();
    }

    /// <summary>The subject's leave requests created in [from, to), of any status, as info. CanOpen follows
    /// GetDetailAsync: requester (or who submitted it for them), an approver at any step, leave:read, or added to the chat.</summary>
    public async Task<IReadOnlyList<InboxPersonCase>> ListPersonCasesAsync(
        Guid tenantId, Guid subjectEmployeeId, string category, DateTimeOffset from, DateTimeOffset to, Guid callerEmployeeId, CancellationToken ct)
    {
        if (category != InboxCategories.Request)
            return [];
        var rows = await _requests.ListOwnAsync(tenantId, subjectEmployeeId,
            new LeaveRequestListFilter(null, null, null, null, CreatedFrom: from, CreatedTo: to), ct);
        if (rows.Count == 0)
            return [];

        var caller = await _employees.GetByIdAsync(tenantId, callerEmployeeId, ct);
        var subject = (await _employees.ListByIdsAsync(tenantId, [subjectEmployeeId], ct)).GetValueOrDefault(subjectEmployeeId);
        var approvers = await _requests.ListApproverEmployeeIdsAsync(tenantId, rows.Select(r => r.Request.Id).ToList(), ct);
        var added = (await _conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct)).ToHashSet();
        var reader = _currentUser.HasPermission(LeaveReadPermission);
        return rows.Select(row =>
        {
            var request = row.Request;
            var canOpen = reader
                || request.EmployeeId == callerEmployeeId
                || (caller is not null && request.SubmittedOnBehalfOfBy == caller.UserId)
                || approvers.GetValueOrDefault(request.Id)?.Contains(callerEmployeeId) == true
                || added.Contains(new ItemRef(Source, LeaveInboxKinds.Request, request.Id));
            return new InboxPersonCase(Item(ListRow.Of(request, subject, null, row.LeaveTypeName), InboxDirections.Info, pending: IsOpen(request), []), canOpen);
        }).ToList();
    }

    public async Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        var items = await ListAsync(tenantId, new InboxQuery(), callerEmployeeId, ct);
        static InboxFilterOption Option(string value, string label) => new(value, label);
        return
        [
            new("leave_type", "Leave type", true, items.Select(i => (Id: i.Facets["leave_type"][0], Name: i.ActionType))
                .DistinctBy(t => t.Id).OrderBy(t => t.Name).Select(t => Option(t.Id, t.Name)).ToList()),
            new("employee", "Employee", true, items.Where(i => i.RequestedBy is not null).Select(i => i.RequestedBy!)
                .DistinctBy(p => p.Id).OrderBy(p => p.Name).Select(p => Option(p.Id.ToString(), p.Name)).ToList()),
        ];
    }

    // ---- detail ---------------------------------------------------------------------------------

    public async Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
    {
        if (kind != LeaveInboxKinds.Request)
            return null;
        var state = await _approvals.GetStateAsync(tenantId, id, ct);
        var caller = await _employees.GetByIdAsync(tenantId, callerEmployeeId, ct);
        if (state is null || caller is null)
            return null;

        var actionable = LeavePendingApprovals.IsActionable(state, caller.Id) && _currentUser.HasPermission(LeaveApprovePermission);
        var isRequester = state.Request.EmployeeId == caller.Id || state.Request.SubmittedOnBehalfOfBy == caller.UserId;
        var isApprover = state.Approvers.Any(a => a.ApproverEmployeeId == caller.Id);
        var added = !isRequester && !isApprover
            && (await _conversations.ListAddedSubjectsAsync(tenantId, caller.Id, Source, ct)).Contains(new ItemRef(Source, kind, id));
        if (!isRequester && !isApprover && !added && !_currentUser.HasPermission(LeaveReadPermission))
            return null;

        var actions = new List<InboxAction>();
        if (actionable)
            actions.AddRange(ReceivedActions());
        else if (isApprover && _currentUser.HasPermission(LeaveApprovePermission)
                 && LeaveDecisionChangeRules.CanChange(state, caller.Id, _clock.UtcNow))
            actions.Add(new InboxAction(LeaveInboxActions.ChangeDecision, "Change decision", "neutral", NeedsComment: true, NeedsPayload: true));
        // Only the requester can cancel from the Inbox; approvers decide (approve/reject/change decision).
        if (isRequester)
            actions.AddRange(SentActions(state.Request, caller.Id));

        var direction = actionable || isApprover ? InboxDirections.Received : isRequester ? InboxDirections.Sent : InboxDirections.Info;
        var item = Item(ListRow.Of(state), direction, actionable || (isRequester && IsOpen(state.Request)), actions);
        var people = await _employees.ListByIdsAsync(tenantId, state.Approvers.Select(a => a.ApproverEmployeeId).Distinct().ToList(), ct);
        return new InboxItemDetail(
            item, Steps(state, people, actionable ? caller.Id : null), Summary(state), ChangeSet(state, actionable),
            state.Request.Reason, Details(state), null,
            JsonSerializer.Serialize(new { startAt = state.Request.StartAt, endAt = state.Request.EndAt }),
            await ExtraAsync(tenantId, state, ct));
    }

    // ---- actions --------------------------------------------------------------------------------

    public async Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
    {
        if (kind != LeaveInboxKinds.Request)
            return Result<InboxActionResult>.UnprocessableEntity(UnknownAction);
        var comment = BodyString(body, "comment");
        // The Inbox calls the Leave commands directly, so it must hold the same permission the Leave endpoints require.
        if (!MayRun(action))
            return Result<InboxActionResult>.Forbidden(MissingPermission);
        if (action == LeaveInboxActions.Cancel)
            return await CancelAsync(tenantId, id, comment, BodyString(body, "effectiveDate"), callerEmployeeId, ct);

        var before = await _approvals.GetStateAsync(tenantId, id, ct);
        if (before is null)
            return Result<InboxActionResult>.NotFound("Leave request not found.");
        var beforeJson = LeaveDecisionSnapshot.Of(before).ToJson();
        // The approve form always carries the dates; only changed dates take the edit-and-approve path.
        var edited = EditedRange(body) is { } range && (range.StartAt != before.Request.StartAt || range.EndAt != before.Request.EndAt)
            ? range : ((DateTimeOffset StartAt, DateTimeOffset EndAt)?)null;

        Func<CancellationToken, Task<Result<LeaveApprovalDecisionResponse>>>? run = action switch
        {
            LeaveInboxActions.Approve => async t => edited is { } newRange
                ? await _sender.Send(new ApproveEditedLeaveRequestCommand(id, newRange.StartAt, newRange.EndAt, comment), t)
                : await _sender.Send(new ApproveLeaveRequestCommand(id, comment), t),
            LeaveInboxActions.Reject => async t => await _sender.Send(new RejectLeaveRequestCommand(id, comment ?? string.Empty), t),
            LeaveInboxActions.ChangeDecision => async t =>
                await _sender.Send(new ChangeLeaveDecisionCommand(id, BodyString(body, "decision") ?? string.Empty, comment), t),
            _ => null,
        };

        if (run is null)
            return Result<InboxActionResult>.UnprocessableEntity(UnknownAction);

        return await JournaledAsync(tenantId, id, action, callerEmployeeId, comment, beforeJson, run, ct);
    }

    public async Task<IReadOnlyList<(ItemRef Ref, Result<InboxActionResult> Result)>?> ExecuteBulkAsync(
        Guid tenantId, string kind, string action, IReadOnlyList<Guid> ids, JsonElement body, Guid callerEmployeeId, CancellationToken ct)
    {
        if (kind != LeaveInboxKinds.Request || action is not (LeaveInboxActions.Approve or LeaveInboxActions.Reject))
            return null;
        if (!MayRun(action))
            return ids.Select(id => (new ItemRef(Source, kind, id), Result<InboxActionResult>.Forbidden(MissingPermission))).ToList();
        var comment = BodyString(body, "comment");
        if (action == LeaveInboxActions.Reject && string.IsNullOrWhiteSpace(comment))
            return ids.Select(id => (new ItemRef(Source, kind, id), Result<InboxActionResult>.UnprocessableEntity("A reason is required to reject."))).ToList();

        var before = new Dictionary<Guid, string>();
        foreach (var id in ids)
        {
            if (await _approvals.GetStateAsync(tenantId, id, ct) is { } state)
                before[id] = LeaveDecisionSnapshot.Of(state).ToJson();
        }

        var bulk = action == LeaveInboxActions.Approve
            ? await _sender.Send(new BulkApproveLeaveRequestsCommand(ids, comment), ct)
            : await _sender.Send(new BulkRejectLeaveRequestsCommand(ids, comment!), ct);
        if (!bulk.IsSuccess)
            return ids.Select(id => (new ItemRef(Source, kind, id), Result<InboxActionResult>.Failure(bulk.Error!, bulk.StatusCode ?? 400))).ToList();

        var results = new List<(ItemRef, Result<InboxActionResult>)>();
        foreach (var item in bulk.Value!.Items)
        {
            var itemRef = new ItemRef(Source, kind, item.RequestId);
            if (!item.Success)
            {
                results.Add((itemRef, Result<InboxActionResult>.UnprocessableEntity(item.Error ?? "The action failed.")));
                continue;
            }
            var after = await _approvals.GetStateAsync(tenantId, item.RequestId, ct);
            await RecordAsync(tenantId, item.RequestId, action, callerEmployeeId, comment, before.GetValueOrDefault(item.RequestId), after, ct);
            results.Add((itemRef, Result<InboxActionResult>.Success(new InboxActionResult(after is null ? [] : People(after)))));
        }
        return results;
    }

    private async Task<Result<InboxActionResult>> CancelAsync(
        Guid tenantId, Guid id, string? reason, string? effectiveDate, Guid callerEmployeeId, CancellationToken ct)
    {
        var before = await _approvals.GetStateAsync(tenantId, id, ct);
        if (before is null)
            return Result<InboxActionResult>.NotFound("Leave request not found.");
        if (before.Request.EmployeeId != callerEmployeeId)
            return Result<InboxActionResult>.Forbidden("Only the requester can cancel this leave request.");
        DateOnly? effective = DateOnly.TryParse(effectiveDate, CultureInfo.InvariantCulture, out var date) ? date : null;
        var beforeJson = LeaveDecisionSnapshot.Of(before).ToJson();
        return await JournaledAsync(tenantId, id, LeaveInboxActions.Cancel, callerEmployeeId, reason, beforeJson, async t =>
        {
            var cancelled = await _sender.Send(new CancelLeaveRequestCommand(id, reason, effective, null), t);
            return cancelled.IsSuccess
                ? Result<LeaveApprovalDecisionResponse>.Success(null!)
                : Result<LeaveApprovalDecisionResponse>.Failure(cancelled.Error!, cancelled.StatusCode ?? 400);
        }, ct);
    }

    /// <summary>Runs the module command and writes its journal row in one transaction; a module failure leaves nothing.</summary>
    private async Task<Result<InboxActionResult>> JournaledAsync(
        Guid tenantId, Guid id, string action, Guid callerEmployeeId, string? comment, string beforeJson,
        Func<CancellationToken, Task<Result<LeaveApprovalDecisionResponse>>> run, CancellationToken ct)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async t =>
            {
                var result = await run(t);
                if (!result.IsSuccess)
                    throw new ModuleRefused(Result<InboxActionResult>.Failure(result.Error!, result.StatusCode ?? 400));
                var after = await _approvals.GetStateAsync(tenantId, id, t);
                await RecordAsync(tenantId, id, action, callerEmployeeId, comment, beforeJson, after, t);
                return Result<InboxActionResult>.Success(new InboxActionResult(after is null ? [] : People(after)));
            }, ct);
        }
        catch (ModuleRefused refused)
        {
            _unitOfWork.ClearTracking();
            return refused.Result;
        }
    }

    private async Task RecordAsync(
        Guid tenantId, Guid id, string action, Guid actor, string? comment, string? beforeJson, LeaveApprovalState? after, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        await _journal.RecordAsync(new InboxDecisionJournalEntry
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Source = Source, Kind = LeaveInboxKinds.Request, ItemId = id, Action = action,
            ActorEmployeeId = actor, BeforeJson = beforeJson, AfterJson = after is null ? null : LeaveDecisionSnapshot.Of(after).ToJson(),
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), DecidedAt = now, CreatedAt = now,
        }, ct);
    }

    private sealed class ModuleRefused(Result<InboxActionResult> result) : Exception(result.Error)
    {
        public Result<InboxActionResult> Result { get; } = result;
    }

    /// <summary>The permission each Leave endpoint requires (LeaveApprovalsController / LeaveRequestsController).</summary>
    private bool MayRun(string action) => action switch
    {
        LeaveInboxActions.Approve or LeaveInboxActions.Reject
            or LeaveInboxActions.ChangeDecision => _currentUser.HasPermission(LeaveApprovePermission),
        LeaveInboxActions.Cancel => _currentUser.HasPermission(LeaveReadOwnPermission) || _currentUser.HasPermission(LeaveManagePermission),
        _ => true,
    };

    // ---- mapping --------------------------------------------------------------------------------

    public static IReadOnlyCollection<Guid> People(LeaveApprovalState state)
        => state.Approvers.Select(a => a.ApproverEmployeeId).Append(state.Request.EmployeeId).Distinct().ToList();

    private static bool IsOpen(LeaveRequest request)
        => request.Status == LeaveRequestStatuses.Pending;

    private bool IsCancellable(LeaveRequest request)
        => request.Status is LeaveRequestStatuses.Pending or LeaveRequestStatuses.Approved
           && request.EndAt > _clock.UtcNow;

    private static List<InboxAction> ReceivedActions() =>
    [
        new(LeaveInboxActions.Approve, "Approve", "primary", NeedsComment: false, NeedsPayload: false),
        new(LeaveInboxActions.Reject, "Reject", "danger", NeedsComment: true, NeedsPayload: false),
    ];

    private List<InboxAction> SentActions(LeaveRequest request, Guid caller)
    {
        var actions = new List<InboxAction>();
        if (IsCancellable(request))
            actions.Add(new InboxAction(LeaveInboxActions.Cancel, "Cancel request", "danger", NeedsComment: true, NeedsPayload: false));
        return actions;
    }

    /// <summary>What a list row needs - built from a full state or, in the list path, from a repository row plus one
    /// batched employee lookup.</summary>
    private sealed record ListRow(
        LeaveRequest Request, string EmployeeName, Guid? LegalEntityId, Guid? DepartmentId, string TypeName, Guid? RequestedTo)
    {
        public static ListRow Of(LeaveApprovalState state) => new(
            state.Request, $"{state.Employee.FirstName} {state.Employee.LastName}".Trim(), state.Employee.LegalEntityId,
            state.Employee.DepartmentId, state.LeaveTypeName,
            state.Approvers.Where(a => a.Status == LeaveRequestApproverStatuses.Pending)
                .OrderBy(a => a.SequenceOrder).Select(a => (Guid?)a.ApproverEmployeeId).FirstOrDefault());

        public static ListRow Of(LeaveRequest request, ONEVO.Domain.Features.CoreHr.Entities.Employee? employee, string? employeeName, string typeName)
            => new(request, employeeName ?? (employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim()),
                employee?.LegalEntityId, employee?.DepartmentId, typeName, null);
    }

    private static InboxItem Item(ListRow row, string direction, bool pending, IReadOnlyList<InboxAction> actions)
    {
        var request = row.Request;
        var facets = new Dictionary<string, IReadOnlyList<string>>
        {
            ["leave_type"] = [request.LeaveTypeId.ToString()],
            ["employee"] = [request.EmployeeId.ToString()],
        };
        if (row.DepartmentId is { } department)
            facets["department"] = [department.ToString()];
        return new InboxItem(
            LeaveInboxKinds.Source, LeaveInboxKinds.Request, request.Id, InboxCategories.Request, row.TypeName,
            $"{row.TypeName} - {row.EmployeeName}", Subtitle(row.TypeName, request), request.Status, direction, null,
            "legal_entity", row.LegalEntityId, null,
            new InboxPerson(request.EmployeeId, row.EmployeeName), row.RequestedTo is { } to ? new InboxPerson(to, string.Empty) : null,
            request.CreatedAt, request.UpdatedAt ?? request.CreatedAt, actions, 0, pending, facets);
    }

    private static string Subtitle(string typeName, LeaveRequest request)
        => $"{typeName} · {request.StartAt:dd MMM} – {request.EndAt:dd MMM} · {request.TotalHours:0.##} h";

    private static string Summary(LeaveApprovalState state)
        => $"{state.Employee.FirstName} {state.Employee.LastName}".Trim() + " requested " + Subtitle(state.LeaveTypeName, state.Request);

    private static IReadOnlyList<InboxChange> ChangeSet(LeaveApprovalState state, bool editable)
    {
        var request = state.Request;
        var remaining = state.Entitlement is null
            ? (decimal?)null
            : LeaveApprovalMapper.CalculateRemaining(
                state.Entitlement.TotalHours, state.Entitlement.CarriedForwardHours, state.Entitlement.UsedHours, state.Entitlement.PendingHours);
        var pending = IsOpen(request);
        return
        [
            new("leave_type", "Leave type", null, state.LeaveTypeName),
            new("startAt", "From", null, request.StartAt.ToString("O"), "datetime", Editable: editable),
            new("endAt", "To", null, request.EndAt.ToString("O"), "datetime", Editable: editable),
            new("total_hours", "Total", null, request.TotalHours.ToString("0.##", CultureInfo.InvariantCulture), "hours"),
            new("paid_hours", "Paid", null, request.PaidHours.ToString("0.##", CultureInfo.InvariantCulture), "hours"),
            new("unpaid_hours", "Unpaid", null, request.UnpaidHours.ToString("0.##", CultureInfo.InvariantCulture), "hours"),
            new("balance", "Balance", Hours(remaining is null ? null : remaining + (pending ? request.PaidHours : 0m)), Hours(remaining), "hours"),
            new("reason", "Reason", null, request.Reason),
        ];
    }

    private static string? Hours(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);

    private static IReadOnlyDictionary<string, string> Details(LeaveApprovalState state)
    {
        var details = new Dictionary<string, string> { ["Status"] = state.Request.Status };
        if (state.Request.NoticePeriodMissed)
            details["Notice"] = "Submitted inside the notice period";
        if (state.Request.CancellationReason is { } cancel)
            details["Cancellation"] = cancel;
        return details;
    }

    private async Task<string> ExtraAsync(Guid tenantId, LeaveApprovalState state, CancellationToken ct)
    {
        var documents = await _approvals.CountDocumentsAsync(tenantId, state.Request.Id, ct);
        return JsonSerializer.Serialize(new
        {
            conflictSnapshot = state.Request.ConflictSnapshotJson is { } json ? JsonSerializer.Deserialize<JsonElement>(json) : (JsonElement?)null,
            documentCount = documents,
            paidHours = state.Request.PaidHours,
            unpaidHours = state.Request.UnpaidHours,
            approvalMode = state.ApprovalMode,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static IReadOnlyList<InboxStep> Steps(
        LeaveApprovalState state, IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee> people, Guid? currentApprover)
    {
        string Name(Guid id) => people.TryGetValue(id, out var p) ? $"{p.FirstName} {p.LastName}".Trim() : "Approver";
        var steps = new List<InboxStep>
        {
            new("submitted", "Submitted", new InboxPerson(state.Request.EmployeeId, $"{state.Employee.FirstName} {state.Employee.LastName}".Trim()),
                state.Request.CreatedAt, InboxStepStates.Done, null),
        };
        foreach (var row in state.Approvers.OrderBy(a => a.SequenceOrder).ThenBy(a => a.DecidedAt ?? DateTimeOffset.MaxValue))
        {
            var label = row.Status switch
            {
                LeaveRequestApproverStatuses.Approved => "Approved",
                LeaveRequestApproverStatuses.Rejected => "Rejected",
                LeaveRequestApproverStatuses.Skipped => "Skipped",
                LeaveRequestApproverStatuses.Cancelled => "Cancelled",
                _ => "Waiting for approval",
            };
            if (row.DelegatedFromApproverId is { } from)
                label += $" (delegate for {Name(from)})";
            var stepState = row.Status switch
            {
                LeaveRequestApproverStatuses.Pending
                    => row.ApproverEmployeeId == currentApprover ? InboxStepStates.Current : InboxStepStates.Upcoming,
                _ => InboxStepStates.Done,
            };
            steps.Add(new InboxStep($"approver:{row.Id}", label, new InboxPerson(row.ApproverEmployeeId, Name(row.ApproverEmployeeId)),
                row.DecidedAt, stepState, row.Comment));
        }
        if (state.Request.Status is LeaveRequestStatuses.Approved or LeaveRequestStatuses.Rejected or LeaveRequestStatuses.Cancelled)
            steps.Add(new InboxStep("final", state.Request.Status switch
            {
                LeaveRequestStatuses.Approved => "Leave approved",
                LeaveRequestStatuses.Rejected => "Leave rejected",
                _ => "Leave cancelled",
            }, null, state.Request.UpdatedAt, InboxStepStates.Done, state.Request.CancellationReason));
        return steps;
    }

    private static (DateTimeOffset StartAt, DateTimeOffset EndAt)? EditedRange(JsonElement? body)
    {
        var json = BodyString(body, "editedPayloadJson");
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.TryGetProperty("startAt", out var s) && root.TryGetProperty("endAt", out var e)
                   && s.TryGetDateTimeOffset(out var start) && e.TryGetDateTimeOffset(out var end)
                ? (start, end)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? BodyString(JsonElement? body, string property)
        => body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
