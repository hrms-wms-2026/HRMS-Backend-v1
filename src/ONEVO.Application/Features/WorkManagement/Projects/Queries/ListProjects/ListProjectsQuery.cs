using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;

public sealed record ListProjectsQuery(
    Guid? TargetUserId,
    PagedRequest Paging
) : IRequest<Result<PagedResult<ProjectListItemResponse>>>;
