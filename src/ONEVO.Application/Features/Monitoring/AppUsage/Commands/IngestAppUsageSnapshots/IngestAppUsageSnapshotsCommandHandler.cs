using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.Mappers;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

public class IngestAppUsageSnapshotsCommandHandler
    : IRequestHandler<IngestAppUsageSnapshotsCommand, Result<SnapshotIngestOutcome>>
{
    internal static readonly TraySnapshotKind Kind = new(
        "App-usage", MonitoringCapability.ApplicationTracking,
        MonitoringErrors.AppTrackingDisabled, MonitoringErrors.AppTrackingDisabledCode);

    private readonly IAppUsageSnapshotRepository _snapshots;
    private readonly TraySnapshotIngest _ingest;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGlobalApplicationRepository _globalApplications;

    public IngestAppUsageSnapshotsCommandHandler(
        IAppUsageSnapshotRepository snapshots,
        TraySnapshotIngest ingest,
        IUnitOfWork unitOfWork,
        IGlobalApplicationRepository globalApplications)
    {
        _snapshots = snapshots;
        _ingest = ingest;
        _unitOfWork = unitOfWork;
        _globalApplications = globalApplications;
    }

    public async Task<Result<SnapshotIngestOutcome>> Handle(
        IngestAppUsageSnapshotsCommand request,
        CancellationToken cancellationToken)
    {
        var begin = await _ingest.BeginAsync(
            Kind, request.Snapshots, s => s.CapturedAt, (s, at) => s with { CapturedAt = at }, cancellationToken);
        if (!begin.IsSuccess)
            return Result<SnapshotIngestOutcome>.Failure(begin.Error!, begin.StatusCode ?? 400, begin.ErrorCode);
        var batch = begin.Value!;

        var alreadyStored = await _snapshots.GetExistingCapturedAtsAsync(
            batch.TenantId, batch.AgentDeviceId, batch.Window.Accepted.Select(s => s.CapturedAt).ToList(), cancellationToken);
        var fresh = _ingest.WithoutStored(Kind, batch, alreadyStored, s => s.CapturedAt);

        var entities = fresh
            .Select(item => AppUsageSnapshotMapper.ToEntity(
                item, batch.TenantId, batch.EmployeeId, batch.AgentDeviceId, batch.Now))
            .ToList();

        if (entities.Count > 0)
        {
            await _snapshots.AddRangeAsync(entities, cancellationToken);
            await _globalApplications.RecordObservedAsync(
                entities.Where(x => x.ProcessName is not null).Select(x => x.ProcessName!),
                batch.Now,
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<SnapshotIngestOutcome>.Success(batch.Window.ToOutcome(entities.Count));
    }
}
