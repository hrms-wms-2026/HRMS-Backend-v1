using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        await SeedAsync(db, db.EmploymentTypes, EmploymentTypes(), "employment types", ct);
        await SeedAsync(db, db.EmploymentStatuses, EmploymentStatuses(), "employment statuses", ct);
        await SeedAsync(db, db.WorkModes, WorkModes(), "work modes", ct);
        await SeedAsync(db, db.ApprovalStatuses, ApprovalStatuses(), "approval statuses", ct);
        await SeedAsync(db, db.Severities, Severities(), "severities", ct);
    }

    private async Task SeedAsync<T>(ApplicationDbContext db, DbSet<T> dbSet, T[] rows, string label, CancellationToken ct)
        where T : class
    {
        if (await dbSet.AnyAsync(ct))
        {
            _logger.LogInformation("Lookup '{Label}' already seeded — skipping.", label);
            return;
        }

        await dbSet.AddRangeAsync(rows, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} {Label}.", rows.Length, label);
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
        new() { Id = 1, Code = "active",     Label = "Active"     },
        new() { Id = 2, Code = "on_leave",   Label = "On Leave"   },
        new() { Id = 3, Code = "suspended",  Label = "Suspended"  },
        new() { Id = 4, Code = "terminated", Label = "Terminated" },
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
