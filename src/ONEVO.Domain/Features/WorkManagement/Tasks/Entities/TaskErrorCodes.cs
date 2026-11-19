namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskErrorCodes
{
    /// <summary>400: a create request's assignee is not the caller, or its planned time is half-given or ends before it starts.</summary>
    public const string InvalidPlanRequest = "invalid_plan_request";
}
