using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DuplicateTask;

public sealed record DuplicateTaskCommand(
    Guid TaskId, Guid DestinationObjectiveId, string Title,
    bool CopyAttachments, bool CopyAssignees, bool CopyComments, bool CopyDueDate
) : IRequest<Result<WorkTaskResponse>>;
