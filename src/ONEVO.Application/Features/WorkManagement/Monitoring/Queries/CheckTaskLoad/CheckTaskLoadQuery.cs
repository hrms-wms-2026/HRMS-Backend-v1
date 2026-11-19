using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Queries.CheckTaskLoad;

/// <summary>Live deadline-load check for the task form: with this task (new, or TaskId edited) added,
/// can each assignee still finish everything due by its due date in their working time?</summary>
public sealed record CheckTaskLoadQuery(
    Guid ProjectId, Guid? TaskId, IReadOnlyList<Guid> AssigneeEmployeeIds, DateOnly? DueDate, decimal? EstimatedHours)
    : IRequest<Result<TaskLoadCheckResponse>>;
