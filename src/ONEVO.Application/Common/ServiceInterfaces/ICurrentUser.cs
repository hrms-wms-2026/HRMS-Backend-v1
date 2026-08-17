namespace ONEVO.Application.Common.ServiceInterfaces;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid TenantId { get; }
    string Email { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasPermission(string permission);
    bool IsAuthenticated { get; }
    string? SessionBinding { get => null; }
    DateTimeOffset? SessionExpiresAt { get => null; }
    Guid? SessionId { get => null; }
}
