using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public sealed record MonitorCaller(Guid TenantId, Guid EmployeeId, Project Project);

/// <summary>The shared gate of the monitor endpoints: an authenticated employee who is an active
/// member of the project (any Module) or its lead.</summary>
public interface IProjectMonitorCallerResolver
{
    Task<Result<MonitorCaller>> ResolveAsync(Guid projectId, CancellationToken ct = default);
}

public sealed class ProjectMonitorCallerResolver : IProjectMonitorCallerResolver
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;

    public ProjectMonitorCallerResolver(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects, IProjectMemberRepository members)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
    }

    public async Task<Result<MonitorCaller>> ResolveAsync(Guid projectId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return Result<MonitorCaller>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (employeeId is null)
            return Result<MonitorCaller>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, projectId, ct);
        if (project is null)
            return Result<MonitorCaller>.NotFound("Project not found.");

        if (project.LeadId != employeeId.Value
            && !await _members.HasActiveMembershipAsync(tenantId, projectId, employeeId.Value, ct))
            return Result<MonitorCaller>.Forbidden("You do not have access to this project.");

        return Result<MonitorCaller>.Success(new MonitorCaller(tenantId, employeeId.Value, project));
    }
}
