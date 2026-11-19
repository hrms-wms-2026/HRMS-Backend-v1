using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionPeopleChart;

public record GetPositionPeopleChartQuery(Guid LegalEntityId)
    : IRequest<Result<IReadOnlyList<PositionPeopleChartNodeResponse>>>;

public class GetPositionPeopleChartQueryHandler
    : IRequestHandler<GetPositionPeopleChartQuery, Result<IReadOnlyList<PositionPeopleChartNodeResponse>>>
{
    private readonly IPositionPeopleChartReader _reader;
    private readonly ICurrentUser _currentUser;

    public GetPositionPeopleChartQueryHandler(IPositionPeopleChartReader reader, ICurrentUser currentUser)
    {
        _reader = reader;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<PositionPeopleChartNodeResponse>>> Handle(
        GetPositionPeopleChartQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<PositionPeopleChartNodeResponse>>.Forbidden("Authentication required.");

        var nodes = await _reader.BuildAsync(_currentUser.TenantId, request.LegalEntityId, ct);
        return Result<IReadOnlyList<PositionPeopleChartNodeResponse>>.Success(nodes);
    }
}
