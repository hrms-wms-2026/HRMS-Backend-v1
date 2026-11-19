using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.AppCatalog;

public sealed class EfGlobalApplicationRepository(ApplicationDbContext db) : IGlobalApplicationRepository
{
    public async Task<IReadOnlyList<GlobalApplication>> ListAsync(CancellationToken ct) =>
        await db.GlobalApplications.AsNoTracking()
            .OrderBy(x => x.Category == null ? 0 : 1)
            .ThenByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.AppName)
            .ToListAsync(ct);

    public Task<GlobalApplication?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.GlobalApplications.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(GlobalApplication application, CancellationToken ct) =>
        await db.GlobalApplications.AddAsync(application, ct);

    public async Task RecordObservedAsync(
        IEnumerable<string> processNames,
        DateTimeOffset observedAt,
        CancellationToken ct)
    {
        foreach (var processName in processNames
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Select(GlobalApplication.NormalizeProcessName)
                     .Distinct(StringComparer.Ordinal))
        {
            // MERGE with HOLDLOCK makes first observation safe when different tenants report
            // the same new process concurrently. Allocation fields are deliberately untouched.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                MERGE global_app_catalog WITH (HOLDLOCK) AS target
                USING (SELECT {processName} AS process_name) AS source
                    ON target.process_name = source.process_name
                WHEN MATCHED AND target.last_seen_at < {observedAt} THEN
                    UPDATE SET last_seen_at = {observedAt}
                WHEN NOT MATCHED THEN
                    INSERT (id, app_name, process_name, is_public, is_productive_default,
                            first_seen_at, last_seen_at, created_at)
                    VALUES ({Guid.NewGuid()}, {GlobalApplication.DefaultDisplayName(processName)}, {processName},
                            1, 0, {observedAt}, {observedAt}, {observedAt});
                """, ct);
        }
    }
}
