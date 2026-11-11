using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;

/// <param name="ForEmployeeId">The principal, for their active planning delegate: read as the principal, only a project holding
/// their assigned task (delegate task planning, 2026-11-11). Null means the caller.</param>
public sealed record GetProjectSprintsQuery(Guid ProjectId, Guid? ForEmployeeId = null) : IRequest<Result<IReadOnlyList<SprintResponse>>>;
