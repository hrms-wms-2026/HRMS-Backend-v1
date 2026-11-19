namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

public sealed record CalendarEventActivityEntryResponse(
    Guid Id, string Action, Guid PerformedById, string? PerformedByName, DateTimeOffset PerformedAt, string DetailsJson);
