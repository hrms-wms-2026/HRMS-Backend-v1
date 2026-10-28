using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Appliers;

/// <summary>
/// Applies an approved meeting.create (spec 5.2): the requester becomes the organizer and the meeting is created
/// exactly as submitted. Stale when the first occurrence has started, the module is gone, or the requester is no
/// longer an active member of it. Applying twice creates one series (the approval_request_id is unique; an existing
/// series counts as applied). Runs in the decide transaction; the factory flushes once because the occurrence insert is
/// raw SQL against the series row.
/// </summary>
public sealed class MeetingCreateApplier : IApprovalActionApplier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<int> ActiveStatuses = [EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave];

    private readonly IOptions<MeetingsOptions> _options;
    private readonly IMeetingSeriesFactory _factory;
    private readonly IMeetingRepository _meetings;
    private readonly IEmployeeRepository _employees;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IDateTimeProvider _clock;

    public MeetingCreateApplier(IOptions<MeetingsOptions> options, IMeetingSeriesFactory factory, IMeetingRepository meetings, IEmployeeRepository employees,
        IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership, IDateTimeProvider clock)
    {
        _options = options;
        _factory = factory;
        _meetings = meetings;
        _employees = employees;
        _objectives = objectives;
        _membership = membership;
        _clock = clock;
    }

    public string ActionType => WorkActionTypes.MeetingCreate;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        // Meetings:ReadOnly is the recovery switch: an approved request stays pending until writes are allowed again.
        if (MeetingGuard.CheckWritable<bool>(_options.Value) is { } blocked)
            return ApplyOutcome.Invalid(blocked.Error!);
        var request = context.Request;
        var form = JsonSerializer.Deserialize<MeetingCreatePayload>(context.PayloadJson, Json)?.Form;
        if (form?.ProjectId is not Guid projectId || form.ObjectiveId is not Guid objectiveId)
            return ApplyOutcome.Invalid("The meeting request is incomplete.");

        if (await _meetings.FindSeriesByApprovalRequestAsync(request.TenantId, request.Id, ct) is { } existing)
        {
            request.TargetId = existing.Id;
            return ApplyOutcome.Applied();
        }

        if (form.Start <= _clock.UtcNow)
            return ApplyOutcome.Stale;
        var module = await _objectives.GetByIdForTenantAsync(request.TenantId, objectiveId, ct);
        if (module is null || !module.IsActive || module.IsAchieved)
            return ApplyOutcome.Stale;
        var requester = await _employees.GetByIdAsync(request.TenantId, request.RequestedByEmployeeId, ct);
        if (requester is null || !ActiveStatuses.Contains(requester.EmploymentStatusId)
            || !await _membership.IsEffectiveManagerAsync(request.TenantId, objectiveId, requester.Id, ct))
            return ApplyOutcome.Stale;

        var created = await _factory.CreateDirectAsync(request.TenantId, requester.Id, requester.UserId,
            new MeetingPlacement(projectId, objectiveId, IsOffice: false, Direct: true), form,
            MeetingCreationPaths.Approval, request.Id, ct);
        if (!created.IsSuccess)
            return ApplyOutcome.Invalid(created.Error ?? "The meeting could not be created.");

        request.TargetId = created.Value!.SeriesId;
        return ApplyOutcome.Applied();
    }
}
