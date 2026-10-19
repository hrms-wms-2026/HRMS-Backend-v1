using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Inbox;

/// <summary>How onboarding approvals are addressed in the Inbox: ("onboarding", kind, requestId).</summary>
public static class OnboardingInboxKinds
{
    public const string Source = "onboarding";
    public const string PositionApproval = OnboardingInboxRowKinds.PositionApproval;
    public const string SeatIncrease = OnboardingInboxRowKinds.SeatIncrease;

    public static readonly InboxKindDescriptor[] Descriptors =
    [
        new(Source, PositionApproval, "Position approval", InboxCategories.Request, InboxCapabilities.Chat),
        new(Source, SeatIncrease, "Seat increase", InboxCategories.Request, InboxCapabilities.Chat),
    ];
}

public static class OnboardingInboxActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
}

/// <summary>
/// Onboarding approvals in the Inbox. Received = the pending requests of a kind the caller approves (position access:
/// roles:manage, seat increase: billing:manage - see <see cref="OnboardingApprovers"/>); decided by me = the ones they
/// decided in the last 90 days; sent = the ones they requested. A position access request is never approved by its
/// requester (the module command refuses it); a billing manager may approve a seat increase they raised themselves - it
/// is their own company's billing, and often they are the only billing manager. Approve/reject run the module's own
/// commands (position access) or raise the subscription's included seats (seat increase).
/// </summary>
public sealed class OnboardingInboxSourceProvider : IInboxSourceProvider
{
    public const int DecidedHistoryDays = 90;
    public const string MissingPermission = "You do not have permission to do this.";
    private const string UnknownAction = "This action is not available for this item.";

    private readonly ICurrentUser _currentUser;
    private readonly IOnboardingInboxRepository _rows;
    private readonly ISeatIncreaseRequestRepository _seatRequests;
    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly OnboardingApprovers _approvers;
    private readonly ISender _sender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public OnboardingInboxSourceProvider(
        ICurrentUser currentUser, IOnboardingInboxRepository rows, ISeatIncreaseRequestRepository seatRequests,
        ITenantSubscriptionRepository subscriptions, OnboardingApprovers approvers, ISender sender, IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _rows = rows;
        _seatRequests = seatRequests;
        _subscriptions = subscriptions;
        _approvers = approvers;
        _sender = sender;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public string Source => OnboardingInboxKinds.Source;
    public string Label => "Onboarding";

    private bool PositionApprover => _currentUser.HasPermission(OnboardingApprovers.PositionApprovalPermission);
    private bool SeatApprover => _currentUser.HasPermission(OnboardingApprovers.SeatIncreasePermission);

    private bool Approves(string kind) => kind == OnboardingInboxKinds.SeatIncrease ? SeatApprover : PositionApprover;

    public async Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct)
    {
        if (query.Category is { Count: > 0 } categories && !categories.Contains(InboxCategories.Request))
            return [];
        var rows = await _rows.ListAsync(tenantId, _currentUser.UserId, PositionApprover, SeatApprover,
            _clock.UtcNow.AddDays(-DecidedHistoryDays), ct);
        return rows.Select(Item).ToList();
    }

    public async Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        var items = await ListAsync(tenantId, new InboxQuery(), callerEmployeeId, ct);
        return
        [
            new("requested_by", "Requested by", true, items.Where(i => i.RequestedBy is not null).Select(i => i.RequestedBy!)
                .DistinctBy(p => p.Id).OrderBy(p => p.Name).Select(p => new InboxFilterOption(p.Id.ToString(), p.Name)).ToList()),
        ];
    }

    public async Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
    {
        var row = await _rows.GetAsync(tenantId, kind, id, ct);
        if (row is null)
            return null;
        var mine = row.RequestedByUserId == _currentUser.UserId || row.DecidedByUserId == _currentUser.UserId;
        if (!mine && !Approves(kind))
            return null;

        var item = Item(row);
        var approvers = kind == OnboardingInboxKinds.SeatIncrease
            ? await _approvers.SeatApproversAsync(tenantId, null, ct)
            : await _approvers.PositionApproversAsync(tenantId, row.RequestedByUserId, ct);
        var changes = new List<InboxChange>();
        if (kind == OnboardingInboxKinds.SeatIncrease)
        {
            changes.Add(new("seats", "Extra seats", null, row.RequestedSeats?.ToString()));
            if (row.Status.Equals(SeatIncreaseRequestStatus.Pending, StringComparison.OrdinalIgnoreCase))
                changes.Add(new("waiting_note", "Note", null, "One seat for each person waiting when this is approved"));
            if (row.SeatsAtRequest is { } seats)
                changes.Add(new("current", "Seats when requested", null, seats.ToString()));
            if (row.SubjectName is { Length: > 0 } waiting)
                changes.Add(new("waiting", "Waiting to join", null, waiting));
        }
        else
        {
            changes.Add(new("employee", "New employee", null, row.SubjectName));
            changes.Add(new("position", "Position", null, row.PositionName));
            if (row.DepartmentName is { } department)
                changes.Add(new("department", "Department", null, department));
            if (row.StartDate is { } start)
                changes.Add(new("start", "Start date", null, start.ToString("yyyy-MM-dd"), "date"));
        }
        changes.Add(new("approvers", "Approvers", null, approvers.Count == 0 ? "Nobody can approve this yet" : string.Join(", ", approvers.Select(a => a.Name))));
        if (row.DecisionNote is { Length: > 0 } note)
            changes.Add(new("note", "Decision note", null, note));

        var explanation = kind == OnboardingInboxKinds.SeatIncrease
            ? "Every included seat is in use, so this person cannot be invited yet. Approving adds the seats to the company's subscription; the waiting draft can then be finalized."
            : "This position's access template needs approval. Approving creates the employee and sends the invitation.";
        return new InboxItemDetail(item, Steps(row), item.Title, changes, explanation, null, null, null);
    }

    public async Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
    {
        if (action is not (OnboardingInboxActions.Approve or OnboardingInboxActions.Reject))
            return Result<InboxActionResult>.UnprocessableEntity(UnknownAction);
        if (!Approves(kind))
            return Result<InboxActionResult>.Forbidden(MissingPermission);
        var row = await _rows.GetAsync(tenantId, kind, id, ct);
        if (row is null)
            return Result<InboxActionResult>.NotFound("Request not found.");
        IReadOnlyCollection<Guid> affected = row.RequesterEmployeeId is { } requester ? [requester, callerEmployeeId] : [callerEmployeeId];
        var note = Comment(body);

        if (kind == OnboardingInboxKinds.PositionApproval)
        {
            if (action == OnboardingInboxActions.Approve)
            {
                var approved = await _sender.Send(new ApproveAccessGrantRequestCommand(id), ct);
                return approved.IsSuccess
                    ? Result<InboxActionResult>.Success(new InboxActionResult(affected, "Approved - the invitation is on its way."))
                    : Result<InboxActionResult>.Failure(approved.Error!, approved.StatusCode ?? 400);
            }
            var rejected = await _sender.Send(new RejectAccessGrantRequestCommand(id, note), ct);
            return rejected.IsSuccess
                ? Result<InboxActionResult>.Success(new InboxActionResult(affected, "Rejected."))
                : Result<InboxActionResult>.Failure(rejected.Error!, rejected.StatusCode ?? 400);
        }

        if (kind != OnboardingInboxKinds.SeatIncrease)
            return Result<InboxActionResult>.UnprocessableEntity(UnknownAction);
        return await DecideSeatIncreaseAsync(tenantId, id, action == OnboardingInboxActions.Approve, note, affected, ct);
    }

    private async Task<Result<InboxActionResult>> DecideSeatIncreaseAsync(
        Guid tenantId, Guid id, bool approve, string? note, IReadOnlyCollection<Guid> affected, CancellationToken ct)
    {
        var request = await _seatRequests.GetTrackedByIdAsync(tenantId, id, ct);
        if (request is null)
            return Result<InboxActionResult>.NotFound("Request not found.");
        if (request.Status != SeatIncreaseRequestStatus.Pending)
            return Result<InboxActionResult>.Conflict("This request was already decided.");
        return await _unitOfWork.ExecuteInTransactionAsync(async t =>
        {
            int? total = null;
            if (approve)
            {
                // Everyone waiting right now, so one approval lets the whole queue (a bulk batch too) through.
                request.RequestedSeats = Math.Max(request.RequestedSeats, (await _rows.ListWaitingForSeatNamesAsync(tenantId, t)).Count);
                var subscription = await _subscriptions.GetByTenantIdAsync(tenantId, t);
                if (subscription is null)
                    return Result<InboxActionResult>.UnprocessableEntity("This company has no subscription to add seats to.");
                subscription.IncludedSeats = (subscription.IncludedSeats ?? 0) + request.RequestedSeats;
                subscription.OverageAllowed ??= false;
                total = subscription.IncludedSeats;
            }
            request.Status = approve ? SeatIncreaseRequestStatus.Approved : SeatIncreaseRequestStatus.Rejected;
            request.DecidedByUserId = _currentUser.UserId;
            request.DecidedAt = _clock.UtcNow;
            request.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            await _seatRequests.SaveChangesAsync(t);
            return Result<InboxActionResult>.Success(new InboxActionResult(affected,
                approve ? $"Approved - the company now has {total} seats." : "Rejected."));
        }, ct);
    }

    // ---- mapping --------------------------------------------------------------------------------

    private InboxItem Item(OnboardingInboxRow row)
    {
        var pending = row.Status.Equals(SeatIncreaseRequestStatus.Pending, StringComparison.OrdinalIgnoreCase);
        var requestedByMe = row.RequestedByUserId == _currentUser.UserId;
        var direction = requestedByMe ? InboxDirections.Sent : InboxDirections.Received;
        var actionable = pending && Approves(row.Kind) && (!requestedByMe || row.Kind == OnboardingInboxKinds.SeatIncrease);
        var title = row.Kind == OnboardingInboxKinds.SeatIncrease
            ? $"Seat increase - {row.RequestedSeats ?? 1} seat{(row.RequestedSeats is > 1 ? "s" : "")}"
            : $"Position approval - {row.SubjectName ?? "New employee"}";
        var subtitle = row.Kind == OnboardingInboxKinds.SeatIncrease
            ? row.SubjectName is { Length: > 0 } waiting ? $"For {waiting}" : row.Reason
            : row.PositionName;
        var requestedBy = new InboxPerson(row.RequesterEmployeeId ?? row.RequestedByUserId, row.RequesterName);
        var facets = new Dictionary<string, IReadOnlyList<string>> { ["requested_by"] = [requestedBy.Id.ToString()] };
        return new InboxItem(
            OnboardingInboxKinds.Source, row.Kind, row.Id, InboxCategories.Request, row.Kind, title, subtitle,
            row.Status.ToLowerInvariant(), direction, null, "legal_entity", row.LegalEntityId, null, requestedBy, null,
            row.RequestedAt, row.DecidedAt ?? row.RequestedAt, actionable ? Actions() : [], 0, pending, facets);
    }

    private static IReadOnlyList<InboxAction> Actions() =>
    [
        new(OnboardingInboxActions.Approve, "Approve", "primary", NeedsComment: false, NeedsPayload: false),
        new(OnboardingInboxActions.Reject, "Reject", "danger", NeedsComment: true, NeedsPayload: false),
    ];

    private static IReadOnlyList<InboxStep> Steps(OnboardingInboxRow row)
    {
        var steps = new List<InboxStep>
        {
            new("requested", "Requested", new InboxPerson(row.RequesterEmployeeId ?? row.RequestedByUserId, row.RequesterName),
                row.RequestedAt, InboxStepStates.Done, row.Reason),
        };
        var decider = row.DecidedByUserId is { } by ? new InboxPerson(by, row.DecidedByName ?? "Approver") : null;
        steps.Add(row.Status.ToLowerInvariant() switch
        {
            SeatIncreaseRequestStatus.Approved => new InboxStep("decided", "Approved", decider, row.DecidedAt, InboxStepStates.Done, row.DecisionNote),
            SeatIncreaseRequestStatus.Rejected => new InboxStep("decided", "Rejected", decider, row.DecidedAt, InboxStepStates.Done, row.DecisionNote),
            _ => new InboxStep("decided", "Waiting for approval", null, null, InboxStepStates.Current, null),
        });
        return steps;
    }

    private static string? Comment(JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b)
            return null;
        foreach (var name in new[] { "comment", "note", "reason" })
            if (b.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }
}
