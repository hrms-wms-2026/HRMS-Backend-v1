using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Mappers;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;

public class IngestActivitySnapshotsCommandHandler
    : IRequestHandler<IngestActivitySnapshotsCommand, Result<SnapshotIngestOutcome>>
{
    internal static readonly TraySnapshotKind Kind = new(
        "Activity", MonitoringCapability.ActivityMonitoring,
        MonitoringErrors.ActivityMonitoringDisabled, MonitoringErrors.ActivityMonitoringDisabledCode);

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IActivitySnapshotRepository _snapshots;
    private readonly IActivityRawBufferRepository _rawBuffer;
    private readonly TraySnapshotIngest _ingest;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestActivitySnapshotsCommandHandler> _logger;

    public IngestActivitySnapshotsCommandHandler(
        IActivitySnapshotRepository snapshots,
        IActivityRawBufferRepository rawBuffer,
        TraySnapshotIngest ingest,
        IUnitOfWork unitOfWork,
        ILogger<IngestActivitySnapshotsCommandHandler> logger)
    {
        _snapshots = snapshots;
        _rawBuffer = rawBuffer;
        _ingest = ingest;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SnapshotIngestOutcome>> Handle(
        IngestActivitySnapshotsCommand request,
        CancellationToken cancellationToken)
    {
        var begin = await _ingest.BeginAsync(
            Kind, request.Snapshots, s => s.CapturedAt, (s, at) => s with { CapturedAt = at }, cancellationToken);
        if (!begin.IsSuccess)
            return Result<SnapshotIngestOutcome>.Failure(begin.Error!, begin.StatusCode ?? 400, begin.ErrorCode);
        var batch = begin.Value!;

        if (request.Snapshots.Count > 50)
        {
            _logger.LogWarning(
                "Large activity snapshot batch. TenantId={TenantId} DeviceId={DeviceId} Count={Count}",
                batch.TenantId,
                batch.AgentDeviceId,
                request.Snapshots.Count);
        }

        // Raw buffer stores the batch for audit/replay — never log payload content.
        var payloadJson = JsonSerializer.Serialize(request.Snapshots, PayloadJsonOptions);
        await _rawBuffer.AddAsync(new ActivityRawBuffer
        {
            Id = Guid.NewGuid(),
            TenantId = batch.TenantId,
            AgentDeviceId = batch.AgentDeviceId,
            ReceivedAt = batch.Now,
            PayloadJson = payloadJson
        }, cancellationToken);

        // Idempotency: GetMyWorkPatternQueryHandler sums ActiveSeconds/IdleSeconds across every row
        // for the day, so inserting the same interval twice silently inflates it.
        var alreadyCaptured = await _snapshots.GetExistingCapturedAtsAsync(
            batch.TenantId, batch.AgentDeviceId, batch.Window.Accepted.Select(s => s.CapturedAt).ToList(), cancellationToken);
        var newSnapshots = _ingest.WithoutStored(Kind, batch, alreadyCaptured, s => s.CapturedAt);

        var entities = newSnapshots
            .Select(item => ActivitySnapshotMapper.ToEntity(
                item, batch.TenantId, batch.EmployeeId, batch.AgentDeviceId, batch.Now))
            .ToList();

        if (entities.Count > 0)
        {
            await _snapshots.AddRangeAsync(entities, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<SnapshotIngestOutcome>.Success(batch.Window.ToOutcome(entities.Count));
    }
}
