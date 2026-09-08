namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class ObjectiveMemberItemViewModel
{
    public Guid EmployeeId { get; set; }
    public string? Name { get; set; }
    public bool IsHead { get; set; }
    public bool Pending { get; set; }
    public string? InviteType { get; set; }
    public Guid? InvitationId { get; set; }
    public DateTimeOffset SinceOrInvitedAt { get; set; }
}

public class ObjectiveMemberListViewModel
{
    public List<ObjectiveMemberItemViewModel> Items { get; set; } = new();
}
