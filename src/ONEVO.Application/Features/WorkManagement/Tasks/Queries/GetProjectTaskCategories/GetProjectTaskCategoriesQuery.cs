using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskCategories;

public sealed record GetProjectTaskCategoriesQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<TaskCategoryResponse>>>;
