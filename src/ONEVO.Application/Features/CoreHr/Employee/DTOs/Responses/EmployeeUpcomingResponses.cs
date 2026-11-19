namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>
/// Kind: calendar | leave | release. The trailing fields feed the overview's item detail popup:
/// calendar events fill Description/MeetingLink/OrganizerName/Timezone/Participants (participant
/// display names), approved leave fills Hours; anything not applicable is null.
/// </summary>
public sealed record EmployeeUpcomingItem(
    string Kind,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool IsAllDay,
    string? Detail,
    string? Description = null,
    string? MeetingLink = null,
    string? OrganizerName = null,
    string? Timezone = null,
    decimal? Hours = null,
    IReadOnlyList<string>? Participants = null);

/// <summary>Items: the earliest `limit` items. Total: how many items the whole window holds, so a client can
/// show the first few and load the rest only on request.</summary>
public sealed record EmployeeUpcomingResponse(DateOnly From, DateOnly To, IReadOnlyList<EmployeeUpcomingItem> Items, int Total);
