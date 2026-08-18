using ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class TransferOutcomeViewModel
{
    public bool Applied { get; set; }
    public ObjectiveChangeRequestViewModel? PendingChangeRequest { get; set; }
    public ProjectMemberInvitationViewModel? PendingInvitation { get; set; }
}
