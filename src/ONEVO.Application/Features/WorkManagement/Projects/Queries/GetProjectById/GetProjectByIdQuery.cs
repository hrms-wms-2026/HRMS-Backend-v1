using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;

/// <param name="ForEmployeeId">The principal, for their active planning delegate: read as the principal, only a project holding
/// their assigned task (delegate task planning, 2026-11-11). Null means the caller.</param>
public sealed record GetProjectByIdQuery(Guid ProjectId, Guid? ForEmployeeId = null) : IRequest<Result<ProjectDetailResponse>>;
