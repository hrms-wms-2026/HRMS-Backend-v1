using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskCategory;

public sealed record EditTaskCategoryCommand(
    // Icon: null keeps the current icon; empty clears it.
    Guid CategoryId, string Name, int DisplayOrder, string? Kind = null, string? Icon = null
) : IRequest<Result>;
