using System.Text.Json;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Appliers;

/// <summary>
/// Applies an approved meeting.move (D9: the destination's D4 rule). Stale when the requester no longer organizes the
/// meeting or is no longer a member of the destination, or when - at apply time - nothing upcoming is left to move.
/// </summary>
public sealed class MeetingMoveApplier : IApprovalActionApplier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly MeetingMoveService _moves;
    private readonly IMeetingRepository _meetings;
    private readonly IEmployeeRepository _employees;
    private readonly IMilestoneMembershipCoordinator _membership;

    public MeetingMoveApplier(MeetingMoveService moves, IMeetingRepository meetings, IEmployeeRepository employees,
        IMilestoneMembershipCoordinator membership)
    {
        _moves = moves;
        _meetings = meetings;
        _employees = employees;
        _membership = membership;
    }

    public string ActionType => WorkActionTypes.MeetingMove;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        var move = JsonSerializer.Deserialize<MeetingMovePayload>(context.PayloadJson, Json);
        if (move is null)
            return ApplyOutcome.Invalid("The move request is incomplete.");

        var series = await _meetings.GetSeriesAsync(request.TenantId, move.SeriesId, ct);
        var requester = await _employees.GetByIdAsync(request.TenantId, request.RequestedByEmployeeId, ct);
        if (series is null || requester is null || series.OrganizerEmployeeId != requester.Id
            || !await _membership.IsEffectiveManagerAsync(request.TenantId, move.ObjectiveId, requester.Id, ct))
            return ApplyOutcome.Stale;

        var applied = await _moves.ApplyAsync(request.TenantId, requester.Id, requester.UserId, move, ct);
        if (applied.IsSuccess)
            return ApplyOutcome.Applied();
        return applied.ErrorCode is MeetingErrorCodes.NothingToMove or MeetingErrorCodes.OccurrenceStarted or MeetingErrorCodes.NotFound
            ? ApplyOutcome.Stale
            : ApplyOutcome.Invalid(applied.Error ?? "The meeting could not be moved.");
    }
}
