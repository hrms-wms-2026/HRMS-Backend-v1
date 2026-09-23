using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProjectCategory;

public sealed record CreateProjectCategoryCommand(string Name) : IRequest<Result<ProjectCategoryListItemResponse>>;
