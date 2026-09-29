using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

public sealed class ExceptionAlertRouterFactory : IExceptionAlertRouterFactory
{
    private readonly IDateTimeProvider _clock;
    private readonly IEmployeeRepository _employees;
    private readonly IPositionAssignmentRepository _positionAssignments;
    private readonly IPositionRepository _positions;
    private readonly IEmployeeHierarchyClosureRepository _closure;
    private readonly IDepartmentRepository _departments;
    private readonly IPermissionRepository _permissions;
    private readonly INotificationDispatcher _notifications;
    private readonly ILoggerFactory? _loggerFactory;

    public ExceptionAlertRouterFactory(
        IDateTimeProvider clock,
        IEmployeeRepository employees,
        IPositionAssignmentRepository positionAssignments,
        IPositionRepository positions,
        IEmployeeHierarchyClosureRepository closure,
        IDepartmentRepository departments,
        IPermissionRepository permissions,
        INotificationDispatcher notifications,
        ILoggerFactory? loggerFactory = null)
    {
        _clock = clock;
        _employees = employees;
        _positionAssignments = positionAssignments;
        _positions = positions;
        _closure = closure;
        _departments = departments;
        _permissions = permissions;
        _notifications = notifications;
        _loggerFactory = loggerFactory;
    }

    public IExceptionAlertRouter CreateForTenant(Guid tenantId)
    {
        var authority = new EmployeeAuthorityResolver(
            new BackgroundTenantUser(tenantId), _clock, _employees, _positionAssignments,
            _positions, _closure, _departments, _permissions);

        return new ExceptionAlertRouter(
            authority, _employees, _permissions, _notifications, _clock,
            _loggerFactory?.CreateLogger<ExceptionAlertRouter>());
    }

    /// <summary>A system actor pinned to one tenant. ResolveApproverAsync only reads TenantId;
    /// the actor holds no permissions and is never treated as a signed-in person.</summary>
    private sealed class BackgroundTenantUser : ICurrentUser
    {
        public BackgroundTenantUser(Guid tenantId) => TenantId = tenantId;

        public Guid UserId => Guid.Empty;
        public Guid TenantId { get; }
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => false;
    }
}
