using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

public interface IMeetingSeriesFactory
{
    /// <summary>
    /// Spec 5.2 direct create, inside the caller's transaction: the series, its attendees (the organizer accepted, the
    /// rest pending; inactive or unknown people skipped and reported), then - under the series lock - the first
    /// occurrence plus every occurrence in the next 7 days. Every check that can fail runs before the first write.
    /// </summary>
    Task<Result<MeetingCreatedResponse>> CreateDirectAsync(Guid tenantId, Guid organizerEmployeeId, Guid organizerUserId,
        MeetingPlacement placement, CreateMeetingRequest request, string creationPath, Guid? approvalRequestId, CancellationToken ct);
}

public sealed class MeetingSeriesFactory : IMeetingSeriesFactory
{
    /// <summary>The rolling window of materialized occurrences (spec D13).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private static readonly HashSet<int> ActiveStatuses = [EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave];

    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingTaskDefaults _defaults;
    private readonly IProjectRepository _projects;
    private readonly IEmployeeRepository _employees;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IMeetingOccurrenceMaterializer _materializer;
    private readonly IMeetingNotifier _notifier;
    private readonly IExternalCalendarConnectionRepository _connections;
    private readonly IMeetingLinkRepository _links;
    private readonly SeriesMeetingLinkService _linkService;

    public MeetingSeriesFactory(IMeetingRepository meetings, IMeetingOccurrenceWriter writer, IMeetingTaskDefaults defaults,
        IProjectRepository projects, IEmployeeRepository employees, IMeetingRecurrence recurrence, IUnitOfWork unitOfWork,
        IDateTimeProvider clock, IMeetingOccurrenceMaterializer materializer, IMeetingNotifier notifier,
        IExternalCalendarConnectionRepository connections, IMeetingLinkRepository links, SeriesMeetingLinkService linkService)
    {
        _notifier = notifier;
        _connections = connections;
        _links = links;
        _linkService = linkService;
        _materializer = materializer;
        _meetings = meetings;
        _writer = writer;
        _defaults = defaults;
        _projects = projects;
        _employees = employees;
        _recurrence = recurrence;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<MeetingCreatedResponse>> CreateDirectAsync(Guid tenantId, Guid organizerEmployeeId, Guid organizerUserId,
        MeetingPlacement placement, CreateMeetingRequest request, string creationPath, Guid? approvalRequestId, CancellationToken ct)
    {
        var project = await _projects.GetByIdForTenantAsync(tenantId, placement.ProjectId, ct);
        if (project is null)
            return Result<MeetingCreatedResponse>.Failure("This module is not available for meetings.", 400, MeetingErrorCodes.ModuleUnavailable);
        var defaults = await _defaults.ResolveAsync(tenantId, project.Id, organizerUserId, ct);
        if (!defaults.IsSuccess)
            return Result<MeetingCreatedResponse>.Failure(defaults.Error!, defaults.StatusCode ?? 400, defaults.ErrorCode);

        var invited = request.ParticipantEmployeeIds.Where(id => id != organizerEmployeeId).Distinct().ToList();
        var found = await _employees.ListByIdsAsync(tenantId, invited, ct);
        var skipped = new List<SkippedParticipant>();
        var attendees = new List<Guid>();
        foreach (var id in invited)
        {
            if (!found.TryGetValue(id, out var employee))
                skipped.Add(new SkippedParticipant(id, "not_found"));
            else if (!ActiveStatuses.Contains(employee.EmploymentStatusId))
                skipped.Add(new SkippedParticipant(id, "inactive"));
            else
                attendees.Add(id);
        }

        var now = _clock.UtcNow;
        var series = new MeetingSeries
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = placement.ProjectId, ObjectiveId = placement.ObjectiveId,
            OrganizerEmployeeId = organizerEmployeeId, Title = request.Title.Trim(), Description = request.Description?.Trim(),
            Location = request.Location?.Trim(), IsPrivate = request.IsPrivate, DtStart = request.Start.ToUniversalTime(),
            DurationMinutes = request.DurationMinutes, IsAllDay = request.IsAllDay, Timezone = request.Timezone,
            RecurrenceRule = request.RecurrenceRule, CreationPath = creationPath, ApprovalRequestId = approvalRequestId,
            CreatedAt = now, CreatedById = organizerUserId,
        };
        _meetings.AddSeries(series);
        var participantRows = attendees.Prepend(organizerEmployeeId).Select(id => new CalendarEventParticipant
        {
            Id = Guid.NewGuid(), TenantId = tenantId, MeetingSeriesId = series.Id, EmployeeId = id,
            ResponseStatus = id == organizerEmployeeId ? CalendarEventParticipantStatuses.Accepted : CalendarEventParticipantStatuses.Pending,
            RespondedAt = id == organizerEmployeeId ? now : null,
        }).ToList();
        var guestRows = MeetingCreateValidator.NormalizeGuests(request.GuestEmails).Select(email => new CalendarEventGuest
        {
            Id = Guid.NewGuid(), TenantId = tenantId, MeetingSeriesId = series.Id, Email = email, CreatedAt = now,
        }).ToList();
        _meetings.AddParticipants(participantRows);
        _meetings.AddGuests(guestRows);
        // The occurrence insert is raw SQL that references the series row.
        await _unitOfWork.SaveChangesAsync(ct);

        await _writer.LockSeriesAsync(tenantId, series.Id, ct);
        var link = await ClaimLinkAsync(tenantId, series, organizerUserId, request.Platform, now, ct);
        var starts = (series.RecurrenceRule is null
                ? [series.DtStart]
                : _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, now, now + Window).Append(series.DtStart))
            .Distinct().Order().ToList();

        Guid? firstTaskId = null;
        foreach (var start in starts)
        {
            var taskId = await _materializer.MaterializeAsync(tenantId, series, start, organizerUserId, ct);
            if (start == series.DtStart)
                firstTaskId = taskId;
        }

        await _notifier.InviteAsync(series, participantRows, guestRows, ct);
        return Result<MeetingCreatedResponse>.Success(
            new MeetingCreatedResponse("created", series.Id, firstTaskId, approvalRequestId, skipped, link));
    }

    /// <summary>A chosen platform claims the series' link and queues its provisioning (after commit, spec 5.7); the invites
    /// wait for the link. Without a usable connection the meeting is created without one and invited at once (P1-5).</summary>
    private async Task<MeetingLinkResponse?> ClaimLinkAsync(Guid tenantId, MeetingSeries series, Guid organizerUserId, string? platform,
        DateTimeOffset now, CancellationToken ct)
    {
        if (platform is null)
            return null;
        var connection = await _connections.GetByTenantUserProviderAsync(tenantId, organizerUserId, CalendarMeetingConnections.SourceFor(platform), ct);
        if (!CalendarMeetingConnections.CanCreateMeetings(connection, platform))
            return new MeetingLinkResponse(null, platform, CalendarMeetingLinkStates.NotConnected, null, null, CalendarMeetingLinkStates.NotConnected, 0);
        var linkId = await _links.TryClaimForSeriesAsync(tenantId, series.Id, connection!.Id, platform, invitesPending: true, now, ct)
            ?? throw new InvalidOperationException($"New series {series.Id} already holds a link.");
        await _unitOfWork.SaveChangesAsync(ct);
        await _linkService.QueueProvisioningAsync(tenantId, linkId, series.Id, generation: 1, ct);
        return new MeetingLinkResponse(linkId, platform, CalendarEventMeetingStatuses.Provisioning, null, null, null, 1);
    }
}
