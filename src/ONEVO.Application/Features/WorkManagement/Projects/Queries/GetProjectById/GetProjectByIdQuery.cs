using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;

public sealed record GetProjectByIdQuery(Guid ProjectId) : IRequest<Result<ProjectDetailResponse>>;
