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
    Guid? TaskId = null);

public sealed record WorkGraphLink(string Source, string Target, string Kind);

public sealed record EmployeeWorkGraphResponse(
    IReadOnlyList<WorkGraphNode> Nodes,
    IReadOnlyList<WorkGraphLink> Links,
    int HiddenTaskCount);
