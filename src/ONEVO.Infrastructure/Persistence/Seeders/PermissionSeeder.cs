using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Authorization;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public class PermissionSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PermissionSeeder> _logger;

    public PermissionSeeder(IServiceProvider services, ILogger<PermissionSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await SeedPermissionsAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Permission seeder could not run because the database or migrated schema is unavailable. " +
                "Apply migrations explicitly with setup-local-db.ps1 -RunMigrations. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedPermissionsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var defined = GetAllPermissions();
        var existingRows = await db.Permissions.ToListAsync(ct);
        var retiredRows = existingRows
            .Where(p => PermissionCatalog.RetiredCodes.Contains(p.Code))
            .ToList();
        if (retiredRows.Count > 0)
        {
            // Role and user-override rows cascade from permissions. Keeping retirement here makes
            // the seeder converge existing databases instead of only affecting fresh installs.
            db.Permissions.RemoveRange(retiredRows);
            existingRows = existingRows.Except(retiredRows).ToList();
        }

        var existing = existingRows
            .Select(p => p.Code)
            .ToHashSet(StringComparer.Ordinal);

        var definedByCode = defined.ToDictionary(p => p.Code, StringComparer.Ordinal);
        var updated = 0;
        foreach (var row in existingRows)
        {
            if (!definedByCode.TryGetValue(row.Code, out var expected))
                continue;

            if (row.Module == expected.Module && row.Description == expected.Description)
                continue;

            row.Module = expected.Module;
            row.Description = expected.Description;
            updated++;
        }

        var toAdd = defined
            .Where(p => !existing.Contains(p.Code))
            .ToList();

        if (toAdd.Count == 0 && updated == 0 && retiredRows.Count == 0)
        {
            _logger.LogInformation("Permissions already seeded â€” skipping.");
            return;
        }

        await db.Permissions.AddRangeAsync(toAdd, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Seeded {Count} new permissions, updated {UpdatedCount}, and retired {RetiredCount} legacy permissions.",
            toAdd.Count, updated, retiredRows.Count);
    }

    /// <summary>The rows this seeder writes, built from <see cref="PermissionCatalog.Seeded"/>.</summary>
    public static List<Permission> GetAllPermissions() =>
        PermissionCatalog.Seeded
            .Select(d => new Permission
            {
                Id = Guid.NewGuid(),
                Code = d.Code,
                Description = d.Description,
                Module = d.Module
            })
            .ToList();
}

