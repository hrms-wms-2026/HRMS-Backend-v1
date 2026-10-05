using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public sealed record CreateTaskStatusChangeRequestRequest(string? Note, TaskStatusChangeSet Changes);
