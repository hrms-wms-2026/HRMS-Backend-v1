using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public interface IWorkCalendarResolver
{
    /// <summary>The working calendar of the project's owning Legal Entity, else the tenant's primary
    /// Legal Entity, else the 8h Monday-Friday default.</summary>
    Task<WorkCalendar> ForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
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
    {
        var project = await _projects.GetByIdForTenantAsync(tenantId, projectId, ct);
        var legalEntity = project is null
            ? null
            : await _legalEntities.GetByIdForTenantAsync(tenantId, project.OwningLegalEntityId, ct);
        legalEntity ??= await _legalEntities.GetPrimaryByTenantIdAsync(tenantId, ct);
        return WorkCapacityCalculator.FromLegalEntity(legalEntity);
    }
}
