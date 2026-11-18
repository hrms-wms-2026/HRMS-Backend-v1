namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

/// <summary>
/// One tenant user a verified Google identity may sign in as. <see cref="MatchedBySubject"/> is
/// true when the user is already linked to the Google subject, false for the verified-email
/// fallback. <see cref="AllowedLoginDomainsJson"/> is the tenant's tenant_auth_policies value
/// (null when the tenant has no policy row or no list).
/// </summary>
public sealed record GoogleLoginCandidateRow(
    Guid TenantId,
    Guid UserId,
    string Slug,
    string DisplayName,
    bool MatchedBySubject,
    string? AllowedLoginDomainsJson);

/// <summary>
/// Sole pre-tenant candidate lookup for base-domain Google login. Backed only by the allowlisted
/// auth_internal.auth_lookup_google_login_candidates procedure — implementations must never query
/// users, tenants, user_external_identities or tenant_auth_policies directly.
/// The procedure returns users linked to the subject; only when there are none, it returns users
/// with the verified email who are not linked to a different Google subject. Tenants whose policy
/// sets google_completion_allowed = 0 are excluded. At most nine rows; a ninth row is the overflow
/// probe.
/// </summary>
public interface IGoogleLoginCandidateRepository
{
    Task<IReadOnlyList<GoogleLoginCandidateRow>> GetCandidatesAsync(
        string providerSubject,
        string normalizedEmail,
        CancellationToken ct = default);
}
