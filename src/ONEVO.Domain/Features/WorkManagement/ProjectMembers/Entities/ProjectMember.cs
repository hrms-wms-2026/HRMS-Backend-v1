using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

public static class ProjectMembershipSources
{
    public const string System = "system";
    public const string ObjectiveInvitation = "objective_invitation";
}

public class ProjectMember : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid EmployeeId { get; set; }
    public string MembershipSource { get; set; } = ProjectMembershipSources.System;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
}
