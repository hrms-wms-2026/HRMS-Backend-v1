using ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class AddObjectiveMemberOutcomeViewModel
{
    public bool AlreadyMember { get; set; }
    public ProjectMemberInvitationViewModel? Invitation { get; set; }
}
