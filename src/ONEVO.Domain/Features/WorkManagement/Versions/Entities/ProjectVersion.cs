using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Versions.Entities;

// Named ProjectVersion, not Version, to avoid colliding with System.Version.
// Maps to the "versions" table via ToTable("versions") in the EF configuration.
public class ProjectVersion : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int StatusId { get; set; } = VersionStatusIds.Planned;
}
