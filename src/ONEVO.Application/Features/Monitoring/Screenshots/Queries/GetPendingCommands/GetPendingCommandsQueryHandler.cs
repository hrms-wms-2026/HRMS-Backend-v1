using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.Mappers;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetPendingCommands;

public class GetPendingCommandsQueryHandler
    : IRequestHandler<GetPendingCommandsQuery, Result<List<AgentCommandDto>>>
{
    private readonly IAgentCommandRepository _commands;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public GetPendingCommandsQueryHandler(
        IAgentCommandRepository commands,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _commands = commands;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<AgentCommandDto>>> Handle(
        GetPendingCommandsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty || _device.DeviceRegistrationId == Guid.Empty)
            return Result<List<AgentCommandDto>>.Failure("A valid tray device token is required.", 401);

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<List<AgentCommandDto>>.Failure("Tenant not found.", 401);

        // Tray requests hit the base host (system mode) — without this, RLS's USING clause
        // silently returns zero rows here instead of erroring, so every poll looks like "no
        // pending commands" even when one exists. See IngestActivitySnapshotsCommandHandler.
        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var pending = await _commands.GetPendingForDeviceAsync(_device.DeviceRegistrationId, cancellationToken);

        if (pending.Count == 0)
            return Result<List<AgentCommandDto>>.Success([]);

        var now = _clock.UtcNow;
        foreach (var cmd in pending)
        {
            cmd.Status = "delivered";
            cmd.DeliveredAt = now;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<List<AgentCommandDto>>.Success(
            pending.Select(AgentCommandMapper.ToDto).ToList());
    }
}
