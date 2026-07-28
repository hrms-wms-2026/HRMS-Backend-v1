namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public sealed record BaseLoginCandidateRow(
    Guid TenantId,
    Guid UserId,
    string Slug,
    string DisplayName,
    string PasswordHash);

/// <summary>
/// Sole pre-tenant candidate lookup path for base-domain credential-first login. Backed only by
/// the allowlisted auth_lookup_base_login_candidates PostgreSQL function — implementations must
/// never query users/tenants directly. Returns at most nine rows ordered by (tenant_id, user_id);
/// a ninth row is the overflow probe, not a real ninth candidate to display.
/// </summary>
public interface IBaseLoginCandidateRepository
{
    Task<IReadOnlyList<BaseLoginCandidateRow>> GetCandidatesAsync(
        string normalizedEmail,
        CancellationToken ct = default);
}
