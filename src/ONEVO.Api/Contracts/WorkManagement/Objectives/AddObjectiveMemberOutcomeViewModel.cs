using ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class AddObjectiveMemberOutcomeViewModel
{
    public bool Applied { get; set; }
    public bool AlreadyMember { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public ProjectMemberInvitationViewModel? Invitation { get; set; }
}
