using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshotUrl;

public class GetScreenshotUrlQueryHandler
    : IRequestHandler<GetScreenshotUrlQuery, Result<ScreenshotUrlDto>>
{
    private static readonly TimeSpan UrlExpiry = TimeSpan.FromMinutes(15);

    private readonly IEvidenceAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public GetScreenshotUrlQueryHandler(
        IEvidenceAssetRepository assets,
        IFileStorageService fileStorage,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IAuditLogRepository auditLogs,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _assets = assets;
        _fileStorage = fileStorage;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditLogs = auditLogs;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<ScreenshotUrlDto>> Handle(
        GetScreenshotUrlQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var asset = await _assets.GetByIdAsync(tenantId, request.EvidenceAssetId, cancellationToken);
        if (asset is null)
            return Result<ScreenshotUrlDto>.Failure(MonitoringErrors.EvidenceAssetNotFound, 404);

        var urlResult = await _fileStorage.GetSignedUrlAsync(tenantId, asset.FileRecordId, UrlExpiry, cancellationToken);
        if (!urlResult.IsSuccess)
            return Result<ScreenshotUrlDto>.Failure(urlResult.Error!, urlResult.StatusCode ?? 404);

        await _auditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = _currentUser.UserId,
            Action = "monitoring.screenshot_viewed",
            ResourceType = "MonitoringEvidenceAsset",
            ResourceId = asset.Id,
            NewValuesJson = JsonSerializer.Serialize(new { employeeId = asset.EmployeeId, evidenceType = asset.EvidenceType }),
            CreatedAt = _clock.UtcNow
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ScreenshotUrlDto>.Success(new ScreenshotUrlDto(urlResult.Value!, _clock.UtcNow.Add(UrlExpiry)));
    }
}
