using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Projects.Entities;

public class ProjectCategory : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
