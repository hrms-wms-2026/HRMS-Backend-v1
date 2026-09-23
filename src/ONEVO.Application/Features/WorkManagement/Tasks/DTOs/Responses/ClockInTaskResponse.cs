namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskStatusMoveInfo(Guid Id, string Name, string Color);

public sealed record ClockInTaskResponse(TaskStatusMoveInfo? MovedToStatus);
