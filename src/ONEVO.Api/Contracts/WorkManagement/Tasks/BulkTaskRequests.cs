namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public sealed record BulkMoveTaskStatusRequest(IReadOnlyList<Guid> TaskIds, Guid NewStatusId);
public sealed record BulkAssignTaskRequest(IReadOnlyList<Guid> TaskIds, Guid EmployeeId);
public sealed record BulkSetTaskPriorityRequest(IReadOnlyList<Guid> TaskIds, string Priority);
public sealed record BulkSetTaskDueDateRequest(IReadOnlyList<Guid> TaskIds, DateOnly? DueDate);
public sealed record BulkConvertTasksToSubtasksRequest(IReadOnlyList<Guid> TaskIds, Guid NewParentTaskId);
public sealed record BulkDeleteTasksRequest(IReadOnlyList<Guid> TaskIds);
