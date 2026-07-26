using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

/// <summary>
/// Executes the allowlisted auth_lookup_base_login_candidates function via a parameterized
/// SELECT. Columns are left unaliased (already snake_case, matching the function's own OUT
/// parameter names) because ApplicationDbContext's UseSnakeCaseNamingConvention() applies to
/// SqlQueryRaw's ad-hoc materialization too - a PascalCase alias would mismatch the naming
/// convention's expected snake_case column name and fail with "required column ... was not
/// present". Materializes into the internal RawRow class (not the public BaseLoginCandidateRow
/// record) because SqlQueryRaw's positional-record materialization is unreliable for multi-word
/// property names; RawRow is then mapped to the public record.
/// </summary>
public sealed class EfBaseLoginCandidateRepository : IBaseLoginCandidateRepository
{
    private readonly ApplicationDbContext _db;

    public EfBaseLoginCandidateRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BaseLoginCandidateRow>> GetCandidatesAsync(
        string normalizedEmail,
        CancellationToken ct = default)
    {
        var rawRows = await _db.Database
            .SqlQueryRaw<RawRow>(
                """
                SELECT tenant_id, user_id, slug, display_name, password_hash
                FROM auth_internal.auth_lookup_base_login_candidates({0})
                """,
                normalizedEmail)
            .ToListAsync(ct);

        var rows = rawRows
            .Select(r => new BaseLoginCandidateRow(r.TenantId, r.UserId, r.Slug, r.DisplayName, r.PasswordHash))
            .ToList();

        return rows;
    }

    private sealed class RawRow
    {
        public Guid TenantId { get; init; }
        public Guid UserId { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
    }
}
