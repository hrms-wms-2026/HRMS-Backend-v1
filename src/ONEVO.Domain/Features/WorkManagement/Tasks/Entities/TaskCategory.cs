using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public class TaskCategory : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
