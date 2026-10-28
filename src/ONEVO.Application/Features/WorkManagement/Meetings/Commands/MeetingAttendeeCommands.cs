using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands;

public sealed record AddSeriesAttendeesCommand(Guid SeriesId, MeetingAttendeesRequest Request) : IRequest<Result<MeetingAttendeesResponse>>;

public sealed record RemoveSeriesEmployeeCommand(Guid SeriesId, Guid EmployeeId) : IRequest<Result<bool>>;

public sealed record RemoveSeriesGuestCommand(Guid SeriesId, string Email) : IRequest<Result<bool>>;

public sealed record AddOccurrenceAttendeesCommand(Guid SeriesId, DateTimeOffset OriginalStart, MeetingAttendeesRequest Request)
    : IRequest<Result<MeetingAttendeesResponse>>;

public sealed record RemoveOccurrenceEmployeeCommand(Guid SeriesId, DateTimeOffset OriginalStart, Guid EmployeeId) : IRequest<Result<bool>>;

public sealed record RemoveOccurrenceGuestCommand(Guid SeriesId, DateTimeOffset OriginalStart, string Email) : IRequest<Result<bool>>;

public sealed record RespondToMeetingCommand(Guid SeriesId, RespondToMeetingRequest Request) : IRequest<Result<bool>>;

/// <summary>The /meetings attendee and RSVP endpoints: resolve the caller's employee, then <see cref="MeetingAttendeeService"/>.</summary>
public sealed class MeetingAttendeeCommandHandlers :
    IRequestHandler<AddSeriesAttendeesCommand, Result<MeetingAttendeesResponse>>,
    IRequestHandler<RemoveSeriesEmployeeCommand, Result<bool>>,
    IRequestHandler<RemoveSeriesGuestCommand, Result<bool>>,
    IRequestHandler<AddOccurrenceAttendeesCommand, Result<MeetingAttendeesResponse>>,
    IRequestHandler<RemoveOccurrenceEmployeeCommand, Result<bool>>,
    IRequestHandler<RemoveOccurrenceGuestCommand, Result<bool>>,
    IRequestHandler<RespondToMeetingCommand, Result<bool>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingAttendeeService _attendees;

    public MeetingAttendeeCommandHandlers(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingAttendeeService attendees)
    {
        _currentUser = currentUser;
        _identity = identity;
        _attendees = attendees;
    }

    private Guid Tenant => _currentUser.TenantId;

    public async Task<Result<MeetingAttendeesResponse>> Handle(AddSeriesAttendeesCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller ? await _attendees.AddSeriesAttendeesAsync(Tenant, caller, c.SeriesId, c.Request, ct)
            : MeetingReadService.NotFound<MeetingAttendeesResponse>();

    public async Task<Result<bool>> Handle(RemoveSeriesEmployeeCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller ? await _attendees.RemoveSeriesEmployeeAsync(Tenant, caller, c.SeriesId, c.EmployeeId, ct)
            : MeetingReadService.NotFound<bool>();

    public async Task<Result<bool>> Handle(RemoveSeriesGuestCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller ? await _attendees.RemoveSeriesGuestAsync(Tenant, caller, c.SeriesId, c.Email, ct)
            : MeetingReadService.NotFound<bool>();

    public async Task<Result<MeetingAttendeesResponse>> Handle(AddOccurrenceAttendeesCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _attendees.AddOccurrenceAttendeesAsync(Tenant, caller, _currentUser.UserId, c.SeriesId, c.OriginalStart, c.Request, ct)
            : MeetingReadService.NotFound<MeetingAttendeesResponse>();

    public async Task<Result<bool>> Handle(RemoveOccurrenceEmployeeCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _attendees.RemoveOccurrenceEmployeeAsync(Tenant, caller, _currentUser.UserId, c.SeriesId, c.OriginalStart, c.EmployeeId, ct)
            : MeetingReadService.NotFound<bool>();

    public async Task<Result<bool>> Handle(RemoveOccurrenceGuestCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _attendees.RemoveOccurrenceGuestAsync(Tenant, caller, _currentUser.UserId, c.SeriesId, c.OriginalStart, c.Email, ct)
            : MeetingReadService.NotFound<bool>();

    public async Task<Result<bool>> Handle(RespondToMeetingCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller ? await _attendees.RespondAsync(Tenant, caller, c.SeriesId, c.Request, ct)
            : MeetingReadService.NotFound<bool>();

    private async Task<Guid?> CallerAsync(CancellationToken ct)
        => _currentUser.IsAuthenticated ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) : null;
}
