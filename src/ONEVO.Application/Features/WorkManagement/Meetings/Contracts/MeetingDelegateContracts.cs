namespace ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

/// <summary>Body of POST /meetings/delegates. <paramref name="ExpiresAt"/> null means until revoked.</summary>
public sealed record AddMeetingDelegateRequest(Guid DelegateEmployeeId, DateTimeOffset? ExpiresAt, bool CanPlanTasks = false);

/// <summary>Body of PATCH /meetings/delegates/{id}: turns "Can also plan my tasks" on or off.</summary>
public sealed record UpdateMeetingDelegateRequest(bool CanPlanTasks);

/// <summary>One of my delegates. <paramref name="State"/> is "active" or "expired".</summary>
public sealed record MeetingDelegateView(Guid Id, Guid EmployeeId, string Name, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string State,
    bool CanPlanTasks = false);

/// <summary>Someone whose meetings I may run.</summary>
public sealed record MeetingPrincipalView(Guid EmployeeId, string Name, bool CanPlanTasks = false);

public static class MeetingDelegateStates
{
    public const string Active = "active";
    public const string Expired = "expired";
}
