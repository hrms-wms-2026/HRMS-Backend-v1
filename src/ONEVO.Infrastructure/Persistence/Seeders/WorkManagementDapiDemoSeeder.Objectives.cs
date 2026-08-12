// Temporary stub - replaced by the real implementation in Task 3. Keeping this as a separate
// partial-class file lets Task 3 land as its own reviewable diff without re-touching
// WorkManagementDapiDemoSeeder.cs above.
namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class WorkManagementDapiDemoSeeder
{
    private static Task SeedProjectsAndObjectivesAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct) => Task.CompletedTask;
}
