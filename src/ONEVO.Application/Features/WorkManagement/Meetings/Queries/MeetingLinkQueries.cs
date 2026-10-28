using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Queries;

public sealed record GetMeetingLinkQuery(Guid SeriesId) : IRequest<Result<MeetingSeriesLinkResponse>>;

/// <summary>GET /meetings/series/{id}/link: the join URL for attendees, the full link state for the organizer, 404 for anyone
/// else - including a caller without an employee record.</summary>
public sealed class MeetingLinkQueryHandler : IRequestHandler<GetMeetingLinkQuery, Result<MeetingSeriesLinkResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly SeriesMeetingLinkService _links;

    public MeetingLinkQueryHandler(ICurrentUser currentUser, ICallerIdentityResolver identity, SeriesMeetingLinkService links)
    {
        _currentUser = currentUser;
        _identity = identity;
        _links = links;
    }

    public async Task<Result<MeetingSeriesLinkResponse>> Handle(GetMeetingLinkQuery q, CancellationToken ct)
        => _currentUser.IsAuthenticated
           && await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) is Guid caller
            ? await _links.GetAsync(_currentUser.TenantId, caller, q.SeriesId, ct)
            : MeetingReadService.NotFound<MeetingSeriesLinkResponse>();
}
