using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands.CreateMeeting;

/// <summary>
/// Spec 5.2. Flags → validation → D4 placement. Direct: the series, attendees and first occurrences in one transaction
/// under the series lock. Member: one meeting.create approval request carrying the form and the D5 snapshot, and
/// nothing else - no task, link or invite until it is approved.
/// </summary>
public sealed class CreateMeetingCommandHandler : IRequestHandler<CreateMeetingCommand, Result<MeetingCreatedResponse>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IOptions<MeetingsOptions> _options;
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingCreateValidator _validator;
    private readonly IMeetingPlacementResolver _placements;
    private readonly IMeetingSeriesFactory _factory;
    private readonly IWorkApprovalEngine _approvals;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreateMeetingCommandHandler(IOptions<MeetingsOptions> options, ICurrentUser currentUser, ICallerIdentityResolver identity,
        MeetingCreateValidator validator, IMeetingPlacementResolver placements, IMeetingSeriesFactory factory,
        IWorkApprovalEngine approvals, IWorkHierarchyService hierarchy, IEmployeeRepository employees, IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _options = options;
        _currentUser = currentUser;
        _identity = identity;
        _validator = validator;
        _placements = placements;
        _factory = factory;
        _approvals = approvals;
        _hierarchy = hierarchy;
        _employees = employees;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<MeetingCreatedResponse>> Handle(CreateMeetingCommand command, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<MeetingCreatedResponse>(_options.Value) is { } blocked)
            return blocked;
        if (!_currentUser.IsAuthenticated)
            return Result<MeetingCreatedResponse>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is not Guid caller)
            return Result<MeetingCreatedResponse>.Forbidden("No employee record for the current user.");

        var request = command.Request;
        var validation = _validator.Validate(request, _clock.UtcNow);
        if (!validation.IsSuccess)
            return Result<MeetingCreatedResponse>.Failure(validation.Error!, validation.StatusCode ?? 400, validation.ErrorCode);

        var placement = await _placements.ResolveAsync(tenantId, caller, request.ProjectId, request.ObjectiveId, ct);
        if (!placement.IsSuccess)
            return Result<MeetingCreatedResponse>.Failure(placement.Error!, placement.StatusCode ?? 400, placement.ErrorCode);
        var at = placement.Value!;

        try
        {
            if (at.Direct)
                return await _unitOfWork.ExecuteInTransactionAsync(innerCt => _factory.CreateDirectAsync(
                    tenantId, caller, _currentUser.UserId, at, request,
                    at.IsOffice ? MeetingCreationPaths.CalendarOffice : MeetingCreationPaths.Direct, approvalRequestId: null, innerCt), ct);

            var payload = JsonSerializer.Serialize(new MeetingCreatePayload(request, await SnapshotAsync(tenantId, caller, at, request, ct)), Json);
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var decision = await _approvals.SubmitAsync(new WorkAction(tenantId, at.ProjectId, caller,
                    WorkActionTypes.MeetingCreate, WorkTargetTypes.Meeting, TargetId: null, request.Title.Trim(), at.ObjectiveId,
                    PositionModuleId: null, payload, TargetUpdatedAt: null), innerCt);
                if (!decision.IsSuccess)
                    return Result<MeetingCreatedResponse>.Failure(decision.Error!, decision.StatusCode ?? 400, decision.ErrorCode);
                if (decision.Value!.IsDirect)   // the engine found the caller above the module after all
                    return await _factory.CreateDirectAsync(tenantId, caller, _currentUser.UserId, at with { Direct = true }, request,
                        MeetingCreationPaths.Direct, approvalRequestId: null, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<MeetingCreatedResponse>.Success(
                    new MeetingCreatedResponse("requested", null, null, decision.Value.ApprovalRequestId, []));
            }, ct);
        }
        catch (MeetingBusyException)
        {
            return MeetingGuard.Busy<MeetingCreatedResponse>();
        }
    }

    /// <summary>The D5 approver view: names and paths resolved now, so later renames never change what was approved.</summary>
    private async Task<MeetingApprovalSnapshot> SnapshotAsync(Guid tenantId, Guid caller, MeetingPlacement at,
        CreateMeetingRequest request, CancellationToken ct)
    {
        var people = await _employees.ListByIdsAsync(tenantId, request.ParticipantEmployeeIds.Append(caller).Distinct().ToList(), ct);
        string Name(Guid id) => people.TryGetValue(id, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : "Unknown";
        var tree = await _hierarchy.LoadTreeAsync(tenantId, at.ProjectId, ct);
        var path = string.Join(" / ", tree.AncestorChain(at.ObjectiveId).Reverse().Select(m => m.Title));

        return new MeetingApprovalSnapshot(request.Title.Trim(), request.Description?.Trim(), request.Start, request.DurationMinutes,
            request.Timezone, request.RecurrenceRule ?? "Does not repeat", Name(caller), path,
            request.ParticipantEmployeeIds.Where(id => id != caller).Distinct().Select(Name).ToList(),
            MeetingCreateValidator.NormalizeGuests(request.GuestEmails), request.Platform);
    }
}
