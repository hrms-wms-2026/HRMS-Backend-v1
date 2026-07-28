using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.Access;
using ONEVO.Application.Features.Users.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

public sealed class MonitoringAccessResolver : IMonitoringAccessResolver
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserProfileRepository _profiles;

    public MonitoringAccessResolver(
        ICurrentUser currentUser,
        IUserProfileRepository profiles)
    {
        _currentUser = currentUser;
        _profiles = profiles;
    }

    public async Task<bool> CanReadEmployeeAsync(
        Guid targetEmployeeId,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated ||
            _currentUser.UserId == Guid.Empty ||
            _currentUser.TenantId == Guid.Empty ||
            targetEmployeeId == Guid.Empty)
            return false;

        var currentEmployee = await _profiles.GetEmployeeByUserIdAsync(
            _currentUser.UserId,
            ct);
        var targetEmployee = await _profiles.GetEmployeeByIdAsync(
            targetEmployeeId,
            ct);

        if (currentEmployee is null ||
            targetEmployee is null ||
            currentEmployee.TenantId != _currentUser.TenantId ||
            targetEmployee.TenantId != _currentUser.TenantId)
            return false;

        if (targetEmployee.Id == currentEmployee.Id)
            return true;

        if (_currentUser.HasPermission("*") ||
            _currentUser.HasPermission("monitoring:configure"))
            return true;

        return targetEmployee.ManagerId == currentEmployee.Id;
    }
}
