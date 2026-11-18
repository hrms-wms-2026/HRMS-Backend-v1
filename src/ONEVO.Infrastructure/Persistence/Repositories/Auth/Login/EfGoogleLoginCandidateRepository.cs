using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

/// <summary>
/// Executes the allowlisted auth_internal.auth_lookup_google_login_candidates procedure via a
/// parameterized EXEC. Same materialization approach as <see cref="EfBaseLoginCandidateRepository"/>:
/// snake_case result columns into an internal class, then mapped to the public record.
/// </summary>
public sealed class EfGoogleLoginCandidateRepository : IGoogleLoginCandidateRepository
{
    private readonly ApplicationDbContext _db;

    public EfGoogleLoginCandidateRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<GoogleLoginCandidateRow>> GetCandidatesAsync(
        string providerSubject,
        string normalizedEmail,
        CancellationToken ct = default)
    {
        var rawRows = await _db.Database
            .SqlQueryRaw<RawRow>(
                """
                EXEC auth_internal.auth_lookup_google_login_candidates @provider_subject = {0}, @normalized_email = {1}
                """,
                providerSubject,
                normalizedEmail)
            .ToListAsync(ct);

        return rawRows
            .Select(r => new GoogleLoginCandidateRow(
                r.TenantId, r.UserId, r.Slug, r.DisplayName, r.MatchedBySubject, r.AllowedLoginDomainsJson))
            .ToList();
    }

    private sealed class RawRow
    {
        public Guid TenantId { get; init; }
        public Guid UserId { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool MatchedBySubject { get; init; }
        public string? AllowedLoginDomainsJson { get; init; }
    }
}
