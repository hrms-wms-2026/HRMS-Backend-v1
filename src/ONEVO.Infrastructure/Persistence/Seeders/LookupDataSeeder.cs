using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public class LookupDataSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LookupDataSeeder> _logger;

    public LookupDataSeeder(IServiceProvider services, ILogger<LookupDataSeeder> logger)
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
            await SeedAllAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Lookup data seeder could not run because the database or migrated schema is unavailable. " +
                "Apply migrations explicitly with setup-local-db.ps1 -RunMigrations. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAllAsync(ApplicationDbContext db, CancellationToken ct)
    {
        await SeedAsync(db, db.EmploymentTypes, EmploymentTypes(), e => e.Id, "employment types", ct);
        await SeedAsync(db, db.EmploymentStatuses, EmploymentStatuses(), e => e.Id, "employment statuses", ct);
        await SeedAsync(db, db.WorkModes, WorkModes(), e => e.Id, "work modes", ct);
        await SeedAsync(db, db.ApprovalStatuses, ApprovalStatuses(), e => e.Id, "approval statuses", ct);
        await SeedAsync(db, db.Severities, Severities(), e => e.Id, "severities", ct);
        await SeedAsync(db, db.VersionStatuses, VersionStatuses(), e => e.Id, "version statuses", ct);
    }

    private static VersionStatus[] VersionStatuses() =>
    [
        new() { Id = VersionStatusIds.Planned,  Code = "planned",  Label = "Planned"  },
        new() { Id = VersionStatusIds.Released, Code = "released", Label = "Released" },
        new() { Id = VersionStatusIds.Archived, Code = "archived", Label = "Archived" },
    ];

    // Row-by-row idempotency (not a table-level "any rows exist" skip) is required because some
    // rows in these tables (e.g. employment_statuses ids 5/6) are seeded directly by EF Core
    // migrations via InsertData, independently of this seeder. A table-level AnyAsync() check
    // would see those migration-inserted rows and skip seeding entirely, silently leaving the
    // baseline ids (e.g. employment_statuses id=1 "active") missing.
    private async Task SeedAsync<T>(
        ApplicationDbContext db, DbSet<T> dbSet, T[] rows, Func<T, int> idSelector, string label, CancellationToken ct)
        where T : class
    {
        var existingIds = (await dbSet.ToListAsync(ct)).Select(idSelector).ToHashSet();
        var missingRows = rows.Where(r => !existingIds.Contains(idSelector(r))).ToArray();

        if (missingRows.Length == 0)
        {
            _logger.LogInformation("Lookup '{Label}' already seeded — skipping.", label);
            return;
        }

        await dbSet.AddRangeAsync(missingRows, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} {Label}.", missingRows.Length, label);
    }

    private static EmploymentType[] EmploymentTypes() =>
    [
        new() { Id = 1, Code = "full_time",  Label = "Full-Time" },
        new() { Id = 2, Code = "part_time",  Label = "Part-Time" },
        new() { Id = 3, Code = "contract",   Label = "Contract"  },
        new() { Id = 4, Code = "intern",     Label = "Intern"    },
    ];

    private static EmploymentStatus[] EmploymentStatuses() =>
    [
        new() { Id = 1, Code = "active",      Label = "Active"      },
        new() { Id = 2, Code = "on_leave",    Label = "On Leave"    },
        new() { Id = 3, Code = "suspended",   Label = "Suspended"   },
        new() { Id = 4, Code = "terminated",  Label = "Terminated"  },
        new() { Id = 5, Code = "offboarding", Label = "Offboarding" },
        new() { Id = 6, Code = "resigned",    Label = "Resigned"    },
    ];

    private static WorkMode[] WorkModes() =>
    [
        new() { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true },
        new() { Id = 2, Code = "remote",  Label = "Remote",  IsActive = true },
        new() { Id = 3, Code = "hybrid",  Label = "Hybrid",  IsActive = true },
    ];

    private static ApprovalStatus[] ApprovalStatuses() =>
    [
        new() { Id = 1, Code = "pending",   Label = "Pending"   },
        new() { Id = 2, Code = "approved",  Label = "Approved"  },
        new() { Id = 3, Code = "rejected",  Label = "Rejected"  },
        new() { Id = 4, Code = "cancelled", Label = "Cancelled" },
    ];

    private static Severity[] Severities() =>
    [
        new() { Id = 1, Code = "low",      Label = "Low"      },
        new() { Id = 2, Code = "medium",   Label = "Medium"   },
        new() { Id = 3, Code = "high",     Label = "High"     },
        new() { Id = 4, Code = "critical", Label = "Critical" },
    ];
}
