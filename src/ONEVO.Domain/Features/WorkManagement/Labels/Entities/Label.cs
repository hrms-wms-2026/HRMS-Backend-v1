using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Labels.Entities;

public class Label : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}
