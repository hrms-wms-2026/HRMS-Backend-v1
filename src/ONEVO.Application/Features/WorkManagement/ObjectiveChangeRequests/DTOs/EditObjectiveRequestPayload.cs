namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;

public sealed record EditObjectiveRequestPayload(string Title, string? Description, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours);
