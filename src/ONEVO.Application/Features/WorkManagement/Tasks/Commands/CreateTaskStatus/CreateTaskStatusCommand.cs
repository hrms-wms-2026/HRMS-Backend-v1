using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public sealed record CreateTaskStatusCommand(
    Guid ProjectId, string Name, int DisplayOrder, string Visibility, bool MarksTaskComplete,
    bool RequiresApproval, Guid? ApproverId
) : IRequest<Result<TaskStatusResponse>>;
