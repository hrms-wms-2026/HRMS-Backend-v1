namespace ONEVO.Application.Common.ServiceInterfaces;

public interface ICurrentUser
{
    Guid UserId { get; }

    /// <summary>
    /// The current tenant. Unlike every other member of this interface, this is NOT strictly
    /// HTTP-only: outside an HTTP request (e.g. a BackgroundService), it falls back to the
    /// ambient <see cref="ITenantContext"/> set by ITenantContextSwitcher.SwitchToTenantAsync.
    /// All other members (UserId, Permissions, IsAuthenticated, etc.) remain
    /// Guid.Empty/false/empty outside an HTTP request - there is no ambient equivalent of
    /// "current user identity" backing them.
    /// </summary>
    Guid TenantId { get; }
    string Email { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasPermission(string permission);
    bool IsAuthenticated { get; }
    string? SessionBinding { get => null; }
    DateTimeOffset? SessionExpiresAt { get => null; }
    Guid? SessionId { get => null; }

    /// <summary>
    /// The tenant user's currently active legal entity/company (Session.ActiveEmployeeId's
    /// Employee.LegalEntityId), resolved server-side per request in
    /// TenantDatabaseTicketStore.RetrieveAsync. Null when the user has no active employee
    /// context (e.g. no Employee row yet). Never sourced from a request body/route value.
    /// </summary>
    Guid? LegalEntityId { get => null; }
}
