using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskCategory;

public sealed record EditTaskCategoryCommand(
    Guid CategoryId, string Name, int DisplayOrder
) : IRequest<Result>;
