using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.DTOs.Responses;

/// <summary>
/// The project-rooted Work Network graph. Deliberately the same shape as
/// <see cref="EmployeeWorkGraphResponse"/>, and built from the same node/link records: the Angular
/// explorer is typed on that shape, which is what lets Work Management reuse it unchanged.
/// </summary>
public sealed record ProjectWorkGraphResponse(
    IReadOnlyList<WorkGraphNode> Nodes,
    IReadOnlyList<WorkGraphLink> Links,
    int HiddenTaskCount);
