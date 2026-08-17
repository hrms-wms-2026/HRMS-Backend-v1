using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;

public sealed record EditTaskStatusCommand(
    Guid StatusId, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, string Visibility
) : IRequest<Result>;
