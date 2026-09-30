using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;

namespace ONEVO.Application.Features.Dashboard.Team.Queries;

/// <summary>Composes the Approvals &amp; Exceptions widget from every registered
/// ITeamActionSource (spec §9.4). Sources run sequentially - the scoped DbContext is not
/// thread-safe, so no Task.WhenAll - and each one is isolated in its own try/catch so one
/// source's failure never takes down the others.</summary>
public sealed class GetTeamActionItemsQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IEnumerable<ITeamActionSource> sources,
    ILogger<GetTeamActionItemsQueryHandler> logger)
    : IRequestHandler<GetTeamActionItemsQuery, Result<TeamActionItemsResponse>>
{
    public async Task<Result<TeamActionItemsResponse>> Handle(GetTeamActionItemsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<TeamActionItemsResponse>.Forbidden();

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return Result<TeamActionItemsResponse>.NotFound("Current employee record was not found.");

        var top = query.TopPerSource <= 0 ? 5 : query.TopPerSource;
        var legalEntityId = actor.LegalEntityId.Value;
        var summaries = new List<ActionSourceSummary>();

        foreach (var source in sources)
        {
            try
            {
                if (!await source.IsGatedAsync(ct))
                    continue; // omitted, not zero (spec §8.2 "Rules")

                summaries.Add(await source.GetSummaryAsync(legalEntityId, top, ct));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Team action source {SourceKey} failed and was reported unavailable", source.Key);
                summaries.Add(ActionSourceSummary.Unavailable(source.Key, source.Domain));
            }
        }

        return Result<TeamActionItemsResponse>.Success(new TeamActionItemsResponse(legalEntityId, summaries));
    }
}
