using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfGlobalEmailDirectoryRepository : IGlobalEmailDirectoryRepository
{
    private readonly ApplicationDbContext _db;

    public EfGlobalEmailDirectoryRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task UpsertAsync(string email, Guid tenantId, CancellationToken ct = default)
    {
        var upsertTask = _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO global_email_directory (email, tenant_id)
            SELECT {email}, {tenantId}
            WHERE NOT EXISTS (
                SELECT 1 FROM global_email_directory WITH (UPDLOCK, HOLDLOCK)
                WHERE email = {email} AND tenant_id = {tenantId})
            """, ct);

        await upsertTask;
    }
}
