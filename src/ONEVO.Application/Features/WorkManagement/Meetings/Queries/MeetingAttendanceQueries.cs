using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Queries;

public sealed record GetMeetingAttendanceQuery(Guid SeriesId, DateTimeOffset OriginalStart) : IRequest<Result<MeetingAttendanceResponse>>;

/// <summary>GET .../occurrences/{originalStart}/attendance: resolve the caller's employee, then
/// <see cref="MeetingAttendanceReadService"/>; a caller without an employee record gets the outsider's 404.</summary>
public sealed class MeetingAttendanceQueryHandler : IRequestHandler<GetMeetingAttendanceQuery, Result<MeetingAttendanceResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingAttendanceReadService _reads;

    public MeetingAttendanceQueryHandler(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingAttendanceReadService reads)
    {
        _currentUser = currentUser;
        _identity = identity;
        _reads = reads;
    }

    public async Task<Result<MeetingAttendanceResponse>> Handle(GetMeetingAttendanceQuery q, CancellationToken ct)
        => _currentUser.IsAuthenticated
           && await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) is Guid caller
            ? await _reads.GetAsync(_currentUser.TenantId, caller, q.SeriesId, q.OriginalStart, ct)
            : MeetingReadService.NotFound<MeetingAttendanceResponse>();
}
