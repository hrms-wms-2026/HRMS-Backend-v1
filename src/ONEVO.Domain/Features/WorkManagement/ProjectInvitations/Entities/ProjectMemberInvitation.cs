using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

public static class ProjectInvitationStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}

public class ProjectMemberInvitation : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid InvitedEmployeeId { get; set; }
    public string Status { get; set; } = ProjectInvitationStatuses.Pending;
    public Guid InvitedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
