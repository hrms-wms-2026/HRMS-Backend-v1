namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

public sealed record SprintActivityResponse(
    Guid Id, Guid EmployeeId, string Action, string? FromStatus, string? ToStatus, string? DetailsJson, DateTimeOffset OccurredAt);
