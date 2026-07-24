using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetSnapshots;

public class GetSnapshotsQueryHandler
    : IRequestHandler<GetSnapshotsQuery, Result<List<ActivitySnapshotDto>>>
{
    private readonly IActivityMonitoringRepository _repo;
    public GetSnapshotsQueryHandler(IActivityMonitoringRepository repo) => _repo = repo;

    public async Task<Result<List<ActivitySnapshotDto>>> Handle(
        GetSnapshotsQuery request, CancellationToken ct)
    {
        var list = await _repo.GetSnapshotsAsync(request.EmployeeId, request.Date, ct);
        var dtos = list.Select(s => new ActivitySnapshotDto(
            s.Id, s.CapturedAt, s.KeyboardEventsCount, s.MouseEventsCount,
            s.ActiveSeconds, s.IdleSeconds, s.IntensityScore, s.ForegroundProcessName)).ToList();
        return Result<List<ActivitySnapshotDto>>.Success(dtos);
    }
}
