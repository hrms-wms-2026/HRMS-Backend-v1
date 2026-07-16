using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.OrgStructure.Entities;

public class Position : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? DefaultRoleId { get; set; }
}
