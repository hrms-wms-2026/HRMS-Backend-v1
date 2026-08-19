using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Settings.Mappers;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Settings.Queries.GetMonitoringFeatureToggles;

public class GetMonitoringFeatureTogglesQueryHandler
    : IRequestHandler<GetMonitoringFeatureTogglesQuery, Result<MonitoringFeatureTogglesResponse>>
{
    private readonly IMonitoringFeatureTogglesRepository _toggles;
    private readonly ICurrentUser _currentUser;

    public GetMonitoringFeatureTogglesQueryHandler(
        IMonitoringFeatureTogglesRepository toggles, ICurrentUser currentUser)
    {
        _toggles = toggles;
        _currentUser = currentUser;
    }

    public async Task<Result<MonitoringFeatureTogglesResponse>> Handle(
        GetMonitoringFeatureTogglesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MonitoringFeatureTogglesResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<MonitoringFeatureTogglesResponse>.Forbidden("Tenant context missing.");

        var entity = await _toggles.GetByTenantIdAsync(tenantId, ct);
        return Result<MonitoringFeatureTogglesResponse>.Success(MonitoringFeatureTogglesMapper.ToResponse(entity));
    }
}
