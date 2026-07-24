using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetAppUsage;

public class GetAppUsageQueryHandler
    : IRequestHandler<GetAppUsageQuery, Result<List<ApplicationUsageDto>>>
{
    private readonly IActivityMonitoringRepository _repo;
    public GetAppUsageQueryHandler(IActivityMonitoringRepository repo) => _repo = repo;

    public async Task<Result<List<ApplicationUsageDto>>> Handle(
        GetAppUsageQuery request, CancellationToken ct)
    {
        var list = await _repo.GetAppUsageAsync(request.EmployeeId, request.Date, ct);
        var dtos = list.Select(u => new ApplicationUsageDto(
            u.Id, u.ProcessName, u.ApplicationName, u.ApplicationCategory,
            u.TotalSeconds, u.IsProductive, u.IsAllowed)).ToList();
        return Result<List<ApplicationUsageDto>>.Success(dtos);
    }
}
