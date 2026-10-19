using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Queries;

public sealed record GetMeetingOccurrenceQuery(Guid TaskId) : IRequest<Result<MeetingOccurrenceResponse>>;

public sealed record GetMeetingOccurrenceAtQuery(Guid SeriesId, DateTimeOffset OriginalStart) : IRequest<Result<MeetingOccurrenceResponse>>;

public sealed record GetMeetingSeriesQuery(Guid SeriesId) : IRequest<Result<MeetingSeriesResponse>>;

public sealed record ListMeetingsQuery(DateTimeOffset From, DateTimeOffset To, Guid? ProjectId) : IRequest<Result<IReadOnlyList<MeetingListItem>>>;

/// <summary>The /meetings reads: resolve the caller's employee, then <see cref="MeetingReadService"/> (one visibility rule).
/// A caller without an employee record sees nothing (404 / empty), like any outsider.</summary>
public sealed class MeetingQueryHandlers :
    IRequestHandler<GetMeetingOccurrenceQuery, Result<MeetingOccurrenceResponse>>,
    IRequestHandler<GetMeetingOccurrenceAtQuery, Result<MeetingOccurrenceResponse>>,
    IRequestHandler<GetMeetingSeriesQuery, Result<MeetingSeriesResponse>>,
    IRequestHandler<ListMeetingsQuery, Result<IReadOnlyList<MeetingListItem>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingReadService _reads;

    public MeetingQueryHandlers(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingReadService reads)
    {
        _currentUser = currentUser;
        _identity = identity;
        _reads = reads;
    }

    public async Task<Result<MeetingOccurrenceResponse>> Handle(GetMeetingOccurrenceQuery q, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _reads.GetOccurrenceAsync(_currentUser.TenantId, caller, q.TaskId, ct)
            : MeetingReadService.NotFound<MeetingOccurrenceResponse>();

    public async Task<Result<MeetingOccurrenceResponse>> Handle(GetMeetingOccurrenceAtQuery q, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _reads.GetOccurrenceAtAsync(_currentUser.TenantId, caller, q.SeriesId, q.OriginalStart, ct)
            : MeetingReadService.NotFound<MeetingOccurrenceResponse>();

    public async Task<Result<MeetingSeriesResponse>> Handle(GetMeetingSeriesQuery q, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _reads.GetSeriesAsync(_currentUser.TenantId, caller, q.SeriesId, ct)
            : MeetingReadService.NotFound<MeetingSeriesResponse>();

    public async Task<Result<IReadOnlyList<MeetingListItem>>> Handle(ListMeetingsQuery q, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _reads.ListAsync(_currentUser.TenantId, caller, q.From, q.To, q.ProjectId, ct)
            : Result<IReadOnlyList<MeetingListItem>>.Success([]);

    private async Task<Guid?> CallerAsync(CancellationToken ct)
        => _currentUser.IsAuthenticated ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) : null;
}
