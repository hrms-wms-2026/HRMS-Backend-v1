namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Kind: calendar | leave | release.</summary>
public sealed record EmployeeUpcomingItem(
    string Kind,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool IsAllDay,
    string? Detail);

public sealed record EmployeeUpcomingResponse(DateOnly From, DateOnly To, IReadOnlyList<EmployeeUpcomingItem> Items);
