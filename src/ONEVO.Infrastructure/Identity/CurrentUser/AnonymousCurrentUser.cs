using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.CurrentUser;

public class AnonymousCurrentUser : ICurrentUser
{
    public Guid UserId => Guid.Empty;
    public Guid TenantId => Guid.Empty;
    public string Email => string.Empty;
    public IReadOnlyList<string> Permissions => [];
    public bool HasPermission(string permission) => false;
    public bool IsAuthenticated => false;
}
