using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateSubtask;

public sealed record CreateSubtaskCommand(
    Guid ParentTaskId, string Title, string? Priority, DateOnly? DueDate, Guid? AssigneeEmployeeId
) : IRequest<Result<WorkTaskResponse>>;
