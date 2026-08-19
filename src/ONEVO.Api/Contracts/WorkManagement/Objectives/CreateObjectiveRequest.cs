namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class CreateObjectiveMemberInvitationRequest
{
    public Guid EmployeeId { get; set; }
    public string Type { get; set; } = "member";
}

public class CreateObjectiveRequest
{
    public Guid ParentObjectiveId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AllocatedHours { get; set; }
    /// <summary>If set and different from the creator, invites this person as leader (pending accept) — does not immediately assign headship.</summary>
    public Guid? HeadEmployeeId { get; set; }
    public List<CreateObjectiveMemberInvitationRequest>? MemberInvitations { get; set; }
}
