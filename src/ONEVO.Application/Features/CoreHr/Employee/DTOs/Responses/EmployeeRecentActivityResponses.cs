namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Action is human wording ("Changed task status"); Target the subject ("WEB-12 Fix cart");
/// Detail an optional qualifier ("→ In review"). Never contains text the employee typed.</summary>
public sealed record EmployeeActivityItem(
    string Id,
    string Kind,
    string Action,
    string? Target,
    string? Detail,
    DateTimeOffset At);

/// <summary>NextBefore is the cursor for the next (older) page, null when there is none. IsPartial is
/// true while the feed is a union over existing tables rather than a complete event log.</summary>
public sealed record EmployeeRecentActivityResponse(
    IReadOnlyList<EmployeeActivityItem> Items,
    DateTimeOffset? NextBefore,
    bool IsPartial);
