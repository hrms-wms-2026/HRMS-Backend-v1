namespace ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;

public sealed record LeaveHolidayResponse(
    Guid Id,
    string Name,
    DateOnly Date,
    DateOnly EndDate);
