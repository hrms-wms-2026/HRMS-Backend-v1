using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCategory;

public sealed record CreateTaskCategoryCommand(
    Guid ProjectId, string Name, int DisplayOrder
) : IRequest<Result<TaskCategoryResponse>>;
