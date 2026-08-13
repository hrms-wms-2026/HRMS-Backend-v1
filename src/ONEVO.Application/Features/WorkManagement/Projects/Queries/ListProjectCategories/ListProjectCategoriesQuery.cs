using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjectCategories;

public sealed record ListProjectCategoriesQuery(bool IncludeInactive) : IRequest<Result<List<ProjectCategoryListItemResponse>>>;
