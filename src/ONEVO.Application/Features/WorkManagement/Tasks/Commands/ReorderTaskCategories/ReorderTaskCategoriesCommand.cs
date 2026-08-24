using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskCategories;

public sealed record TaskCategoryOrderUpdate(Guid CategoryId, int DisplayOrder);

public sealed record ReorderTaskCategoriesCommand(Guid ProjectId, List<TaskCategoryOrderUpdate> Updates) : IRequest<Result<IReadOnlyList<TaskCategoryResponse>>>;
