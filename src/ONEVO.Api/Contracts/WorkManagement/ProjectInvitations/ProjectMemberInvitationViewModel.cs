namespace ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;

public class ProjectMemberInvitationViewModel
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid InvitedEmployeeId { get; set; }
    public string InviteType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid InvitedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
