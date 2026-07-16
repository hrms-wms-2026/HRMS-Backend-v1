using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class RolePermission : ITenantOwnedEntity
{
    public Guid TenantId { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }

    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
