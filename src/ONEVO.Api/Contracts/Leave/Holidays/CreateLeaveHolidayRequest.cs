namespace ONEVO.Api.Contracts.Leave.Holidays;

public sealed record CreateLeaveHolidayRequest(
    string Name,
    DateOnly Date,
    DateOnly? EndDate);
