namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>The single home of "Work I Lead" (My Team spec §8.3, §9.2). Relationship-based only:
/// never touches management coverage.</summary>
public interface IWorkLeadershipService
{
    Task<bool> LeadsAnyWorkAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);

    Task<LedWorkScope> ResolveLedScopeAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);
}
