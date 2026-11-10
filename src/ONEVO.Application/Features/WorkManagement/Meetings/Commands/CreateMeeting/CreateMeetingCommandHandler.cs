using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
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
    private readonly IMeetingApprovalSnapshotBuilder _snapshots;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IMeetingDelegateRepository _delegates;
    private readonly IEmployeeRepository _employees;
    private readonly IMeetingDelegateAudit _audit;

    public CreateMeetingCommandHandler(IOptions<MeetingsOptions> options, ICurrentUser currentUser, ICallerIdentityResolver identity,
        MeetingCreateValidator validator, IMeetingPlacementResolver placements, IMeetingSeriesFactory factory,
        IWorkApprovalEngine approvals, IMeetingApprovalSnapshotBuilder snapshots, IUnitOfWork unitOfWork,
        IDateTimeProvider clock, IMeetingDelegateRepository delegates, IEmployeeRepository employees, IMeetingDelegateAudit audit)
    {
        _delegates = delegates;
        _employees = employees;
        _audit = audit;
        _options = options;
        _currentUser = currentUser;
        _identity = identity;
        _validator = validator;
        _placements = placements;
        _factory = factory;
        _approvals = approvals;
        _snapshots = snapshots;
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

        var organizer = await OrganizerAsync(tenantId, caller, request.OrganizerEmployeeId, ct);
        if (organizer is null)
            return Result<MeetingCreatedResponse>.Failure("You cannot schedule meetings for this person.", 403, MeetingErrorCodes.DelegateRequired);

        // Placement, approval and organizer are all the principal's when a delegate schedules for them.
        var placement = await _placements.ResolveAsync(tenantId, organizer.EmployeeId, request.ProjectId, request.ObjectiveId, ct);
        if (!placement.IsSuccess)
            return Result<MeetingCreatedResponse>.Failure(placement.Error!, placement.StatusCode ?? 400, placement.ErrorCode);
        var at = placement.Value!;

        try
        {
            if (at.Direct)
                return await _unitOfWork.ExecuteInTransactionAsync(innerCt => CreateAndSaveAsync(
                    tenantId, organizer, at, request, at.IsOffice ? MeetingCreationPaths.CalendarOffice : MeetingCreationPaths.Direct, innerCt), ct);

            var payload = JsonSerializer.Serialize(new MeetingCreatePayload(request,
                await _snapshots.ForCreateAsync(tenantId, organizer.EmployeeId, at, request, ct)), Json);
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var decision = await _approvals.SubmitAsync(new WorkAction(tenantId, at.ProjectId, organizer.EmployeeId,
                    WorkActionTypes.MeetingCreate, WorkTargetTypes.Meeting, TargetId: null, request.Title.Trim(), at.ObjectiveId,
                    PositionModuleId: null, payload, TargetUpdatedAt: null), innerCt);
                if (!decision.IsSuccess)
                    return Result<MeetingCreatedResponse>.Failure(decision.Error!, decision.StatusCode ?? 400, decision.ErrorCode);
                if (decision.Value!.IsDirect)   // the engine found the caller above the module after all
                    return await CreateAndSaveAsync(tenantId, organizer, at with { Direct = true }, request, MeetingCreationPaths.Direct, innerCt);

                await AuditAsync(tenantId, organizer, seriesId: null, request, "requested", innerCt);
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

    /// <summary>
    /// The factory sends the invites last, and their in-app notices are tracked outbox rows. Saving here keeps them in the
    /// transaction; without it only the invite emails, written with raw SQL, would be committed.
    /// </summary>
    private async Task<Result<MeetingCreatedResponse>> CreateAndSaveAsync(Guid tenantId, MeetingOrganizer organizer, MeetingPlacement at,
        CreateMeetingRequest request, string creationPath, CancellationToken ct)
    {
        var created = await _factory.CreateDirectAsync(tenantId, organizer.EmployeeId, organizer.UserId, at, request, creationPath,
            approvalRequestId: null, ct, createdByUserId: _currentUser.UserId);
        if (created.IsSuccess)
        {
            await AuditAsync(tenantId, organizer, created.Value!.SeriesId, request, "created", ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        return created;
    }

    /// <summary>Who the meeting is for: the caller, unless the request names someone they are an active delegate of.
    /// Null when it names someone who did not delegate to them (403).</summary>
    private async Task<MeetingOrganizer?> OrganizerAsync(Guid tenantId, Guid caller, Guid? requested, CancellationToken ct)
    {
        if (requested is not Guid principalId || principalId == caller)
            return new MeetingOrganizer(caller, _currentUser.UserId, ForPrincipal: false);
        if (!await _delegates.IsActiveDelegateAsync(tenantId, principalId, caller, _clock.UtcNow, ct))
            return null;
        var principal = await _employees.GetByIdAsync(tenantId, principalId, ct);
        return principal is null ? null : new MeetingOrganizer(principal.Id, principal.UserId, ForPrincipal: true);
    }

    /// <summary>meeting.created_for_principal (spec "Audit log"), only when a delegate created it.</summary>
    private Task AuditAsync(Guid tenantId, MeetingOrganizer organizer, Guid? seriesId, CreateMeetingRequest request, string outcome,
        CancellationToken ct)
        => organizer.ForPrincipal
            ? _audit.RecordForPrincipalAsync(tenantId, seriesId, request.IsPrivate, organizer.EmployeeId, MeetingAuditActions.Created, new
            {
                title = request.Title.Trim(), start = request.Start, durationMinutes = request.DurationMinutes,
                recurrenceRule = request.RecurrenceRule, isPrivate = request.IsPrivate,
                participantCount = request.ParticipantEmployeeIds.Count, guestCount = request.GuestEmails.Count,
                platform = request.Platform, outcome,
            }, null, ct)
            : Task.CompletedTask;

    private sealed record MeetingOrganizer(Guid EmployeeId, Guid UserId, bool ForPrincipal);

}
