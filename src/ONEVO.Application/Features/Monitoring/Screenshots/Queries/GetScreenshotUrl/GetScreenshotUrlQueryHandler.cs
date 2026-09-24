using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshotUrl;

public class GetScreenshotUrlQueryHandler
    : IRequestHandler<GetScreenshotUrlQuery, Result<ScreenshotUrlDto>>
{
    private static readonly TimeSpan UrlExpiry = TimeSpan.FromMinutes(15);

    private readonly IEvidenceAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public GetScreenshotUrlQueryHandler(
        IEvidenceAssetRepository assets,
        IFileStorageService fileStorage,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _assets = assets;
        _fileStorage = fileStorage;
        _tenantContext = tenantContext;
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

        return Result<ScreenshotUrlDto>.Success(new ScreenshotUrlDto(urlResult.Value!, _clock.UtcNow.Add(UrlExpiry)));
    }
}
