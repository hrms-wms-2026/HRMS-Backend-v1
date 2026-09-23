using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Services.TimeAttendance;

// Seeds the 3 default Work Modes for a newly created Legal Entity - Remote/Hybrid/Onsite,
// in that display order, matching the seeded defaults confirmed in the design spec. "Field"
// is deliberately not seeded (spec decision) - an admin creates it manually if wanted.
// WebEnabled=true is the one universally-safe default clock-in method; everything else starts
// off and the admin tunes it. Called from tenant provisioning, ad hoc legal entity creation,
// and the dev smoke seeder, so all three paths produce identical starting data.
public class WorkModeSeeder : IWorkModeSeeder
{
    private readonly IWorkModeRepository _workModes;
    private readonly IDateTimeProvider _clock;

    public WorkModeSeeder(IWorkModeRepository workModes, IDateTimeProvider clock)
    {
        _workModes = workModes;
        _clock = clock;
    }

    public async Task SeedDefaultsAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var defaults = new[] { "Remote", "Hybrid", "Onsite" };

        for (var i = 0; i < defaults.Length; i++)
        {
            await _workModes.AddAsync(new WorkMode
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                LegalEntityId = legalEntityId,
                Name = defaults[i],
                BiometricEnabled = false,
                WebEnabled = true,
                TrayEnabled = false,
                PhotoRequired = false,
                IsSystemSeeded = true,
                DisplayOrder = i,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            }, ct);
        }

        await _workModes.SaveChangesAsync(ct);
    }
}
