namespace ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;

public static class WorkGraphNodeKinds
{
    public const string Employee = "employee";
    public const string Project = "project";
    public const string Module = "module";
    public const string Task = "task";
}

public static class WorkGraphModuleRoles
{
    public const string Owner = "owner";
    public const string Member = "member";
    public const string Contributor = "contributor";
}

public static class WorkGraphLinkKinds
{
    public const string WorksOn = "works_on";
    public const string Contains = "contains";
    public const string Owns = "owns";
    public const string MemberOf = "member_of";
    public const string HasTask = "has_task";
}

public sealed record WorkGraphNode(
    string Id,
    string Kind,
    string Label,
    string? Sublabel = null,
    string? Role = null,
    string? Status = null,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null,
    Guid? TaskId = null,
    WorkGraphStats? Stats = null,
    WorkGraphModuleDetails? Module = null,
    WorkGraphTaskDetails? Task = null,
    // Projects: the cover image's file id (streamed by GET /projects/{id}/logo).
    Guid? LogoFileId = null);

/// <summary>Whole-module / whole-project numbers (every top-level task, not just this employee's).
/// Progress matches Work Management: CompletedHours / AllocatedHours.</summary>
public sealed record WorkGraphStats(
    int TotalTasks, int NotStarted, int InProgress, int Completed, int Overdue,
    decimal AllocatedHours, decimal CompletedHours);

public sealed record WorkGraphPerson(Guid EmployeeId, string Name, Guid? AvatarFileId);

/// <summary>Members is capped (earliest joiners first); MemberCount is the full count.</summary>
public sealed record WorkGraphModuleDetails(
    WorkGraphPerson? Owner, DateOnly StartDate, DateOnly EndDate,
    IReadOnlyList<WorkGraphPerson> Members, int MemberCount);

/// <summary>One of this employee's open tasks. Overdue: due before today.</summary>
public sealed record WorkGraphTaskDetails(DateOnly? DueDate, string? StatusName, string? Priority, bool IsOverdue);

public sealed record WorkGraphLink(string Source, string Target, string Kind);

public sealed record EmployeeWorkGraphResponse(
    IReadOnlyList<WorkGraphNode> Nodes,
    IReadOnlyList<WorkGraphLink> Links,
    int HiddenTaskCount);
