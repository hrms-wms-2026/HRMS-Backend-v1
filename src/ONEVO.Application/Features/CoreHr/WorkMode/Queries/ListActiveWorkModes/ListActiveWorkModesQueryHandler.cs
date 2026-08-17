using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.WorkModes.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.WorkModes.Queries.ListActiveWorkModes;

public sealed class ListActiveWorkModesQueryHandler
    : IRequestHandler<ListActiveWorkModesQuery, Result<List<WorkModeDto>>>
{
    private readonly IWorkModeRepository _workModes;

    public ListActiveWorkModesQueryHandler(IWorkModeRepository workModes) => _workModes = workModes;

    public async Task<Result<List<WorkModeDto>>> Handle(ListActiveWorkModesQuery request, CancellationToken ct)
    {
        var workModes = await _workModes.ListActiveAsync(ct);
        var dtos = workModes.Select(w => new WorkModeDto(w.Id, w.Code, w.Label)).ToList();
        return Result<List<WorkModeDto>>.Success(dtos);
    }
}
