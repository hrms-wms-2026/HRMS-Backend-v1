using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetMeetings;

public class GetMeetingsQueryHandler
    : IRequestHandler<GetMeetingsQuery, Result<List<MeetingSessionDto>>>
{
    private readonly IActivityMonitoringRepository _repo;
    public GetMeetingsQueryHandler(IActivityMonitoringRepository repo) => _repo = repo;

    public async Task<Result<List<MeetingSessionDto>>> Handle(
        GetMeetingsQuery request, CancellationToken ct)
    {
        var list = await _repo.GetMeetingsAsync(request.EmployeeId, request.Date, ct);
        var dtos = list.Select(m => new MeetingSessionDto(
            m.Id, m.MeetingStart, m.MeetingEnd, m.Platform,
            m.DurationMinutes, m.HadCameraOn, m.HadMicActivity)).ToList();
        return Result<List<MeetingSessionDto>>.Success(dtos);
    }
}
