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

    /// <summary>
    /// The tenant user's currently active legal entity/company (Session.ActiveEmployeeId's
    /// Employee.LegalEntityId), resolved server-side per request in
    /// TenantDatabaseTicketStore.RetrieveAsync. Null when the user has no active employee
    /// context (e.g. no Employee row yet). Never sourced from a request body/route value.
    /// </summary>
    Guid? LegalEntityId { get => null; }
}
