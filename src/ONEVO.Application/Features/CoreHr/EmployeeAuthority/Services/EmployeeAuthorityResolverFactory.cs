using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;

/// <summary>
/// Builds an <see cref="IEmployeeAuthorityResolver"/> pinned to one tenant, for code that has no signed-in
/// user (background jobs, escalation policies). ResolveApproverAsync only reads the tenant from ICurrentUser,
/// so the pinned actor holds no permissions and is never treated as a person.
/// </summary>
public interface IEmployeeAuthorityResolverFactory
{
    IEmployeeAuthorityResolver CreateForTenant(Guid tenantId);
}

public sealed class EmployeeAuthorityResolverFactory : IEmployeeAuthorityResolverFactory
{
    private readonly IDateTimeProvider _clock;
    private readonly IEmployeeRepository _employees;
    private readonly IPositionAssignmentRepository _positionAssignments;
    private readonly IPositionRepository _positions;
    private readonly IEmployeeHierarchyClosureRepository _closure;
    private readonly IDepartmentRepository _departments;
    private readonly IPermissionRepository _permissions;

    public EmployeeAuthorityResolverFactory(
        IDateTimeProvider clock, IEmployeeRepository employees, IPositionAssignmentRepository positionAssignments,
        IPositionRepository positions, IEmployeeHierarchyClosureRepository closure, IDepartmentRepository departments,
        IPermissionRepository permissions)
    {
        _clock = clock;
        _employees = employees;
        _positionAssignments = positionAssignments;
        _positions = positions;
        _closure = closure;
        _departments = departments;
        _permissions = permissions;
    }

    public IEmployeeAuthorityResolver CreateForTenant(Guid tenantId)
        => new EmployeeAuthorityResolver(
            new BackgroundTenantUser(tenantId), _clock, _employees, _positionAssignments, _positions, _closure, _departments, _permissions);
}

/// <summary>A system actor pinned to one tenant. ResolveApproverAsync only reads TenantId;
/// the actor holds no permissions and is never treated as a signed-in person.</summary>
public sealed class BackgroundTenantUser : ICurrentUser
{
    public BackgroundTenantUser(Guid tenantId) => TenantId = tenantId;

    public Guid UserId => Guid.Empty;
    public Guid TenantId { get; }
    public string Email => string.Empty;
    public IReadOnlyList<string> Permissions => [];
    public bool HasPermission(string permission) => false;
    public bool IsAuthenticated => false;
}
