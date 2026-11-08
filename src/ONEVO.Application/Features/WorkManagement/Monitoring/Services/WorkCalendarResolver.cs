using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public interface IWorkCalendarResolver
{
    /// <summary>The working calendar of the project's owning Legal Entity, else the tenant's primary
    /// Legal Entity, else the 8h Monday-Friday default.</summary>
    Task<WorkCalendar> ForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>The Legal Entity behind <see cref="ForProjectAsync"/> (the project's owning one, else the tenant's
    /// primary one); null when neither exists. Its zone dates the conversion-rate window.</summary>
    Task<LegalEntity?> LegalEntityForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
}

public sealed class WorkCalendarResolver : IWorkCalendarResolver
{
    private readonly IProjectRepository _projects;
    private readonly ILegalEntityRepository _legalEntities;

    public WorkCalendarResolver(IProjectRepository projects, ILegalEntityRepository legalEntities)
    {
        _projects = projects;
        _legalEntities = legalEntities;
    }

    public async Task<WorkCalendar> ForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => WorkCapacityCalculator.FromLegalEntity(await LegalEntityForProjectAsync(tenantId, projectId, ct));

    public async Task<LegalEntity?> LegalEntityForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdForTenantAsync(tenantId, projectId, ct);
        var legalEntity = project is null
            ? null
            : await _legalEntities.GetByIdForTenantAsync(tenantId, project.OwningLegalEntityId, ct);
        return legalEntity ?? await _legalEntities.GetPrimaryByTenantIdAsync(tenantId, ct);
    }
}
