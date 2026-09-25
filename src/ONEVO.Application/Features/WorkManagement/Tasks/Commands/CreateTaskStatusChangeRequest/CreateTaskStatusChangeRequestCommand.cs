using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatusChangeRequest;

public sealed record CreateTaskStatusChangeRequestCommand(
    Guid ProjectId, string? Note, TaskStatusChangeSet Changes
) : IRequest<Result<TaskStatusChangeRequestResponse>>;
