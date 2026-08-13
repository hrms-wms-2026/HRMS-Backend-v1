using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.WorkSessions.OutboxPayloads;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Application.Features.Monitoring.WorkSessions.Commands.SubmitWorkSession;

public class SubmitWorkSessionCommandHandler
    : IRequestHandler<SubmitWorkSessionCommand, Result<WorkSessionResponseDto>>
{
    private readonly IWorkSessionRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOutboxWriter _outboxWriter;

    public SubmitWorkSessionCommandHandler(
        IWorkSessionRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        IOutboxWriter outboxWriter)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _outboxWriter = outboxWriter;
    }

    public async Task<Result<WorkSessionResponseDto>> Handle(
        SubmitWorkSessionCommand request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<WorkSessionResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<WorkSessionResponseDto>.Failure("Tenant not found.", 401);

        // Tray requests may hit the base host (system mode). Switch into the JWT
        // tenant so EF query filters + PostgreSQL RLS accept the read/write.
        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        // Upsert-by-id: a retried delivery of an already-landed session is a no-op,
        // not a duplicate row or an error.
        var existing = await _repository.FindByIdAsync(request.SessionId, _device.TenantId, cancellationToken);
        if (existing is not null)
            return Result<WorkSessionResponseDto>.Success(new WorkSessionResponseDto(existing.Id));

        var session = new EmployeeWorkSession
        {
            Id = request.SessionId,
            TenantId = _device.TenantId,
            UserId = _device.UserId,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            ClockInAt = request.ClockInAt,
            ClockOutAt = request.ClockOutAt,
            AccumulatedBreakSeconds = request.AccumulatedBreakSeconds,
            AccumulatedWorkSeconds = request.AccumulatedWorkSeconds,
            BreakSessionCount = request.BreakSessionCount,
            ScheduleDisplay = request.ScheduleDisplay,
            CreatedAt = _clock.UtcNow
        };

        await _repository.AddAsync(session, cancellationToken);

        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.MonitoringWorkSessionCompleted,
            new MonitoringWorkSessionCompletedPayload(
                session.Id,
                _device.TenantId,
                _device.UserId,
                request.ClockOutAt),
            _device.TenantId,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<WorkSessionResponseDto>.Success(new WorkSessionResponseDto(session.Id));
    }
}
