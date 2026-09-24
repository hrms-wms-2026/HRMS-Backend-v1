using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshotUrl;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.Screenshots;

public class GetScreenshotUrlQueryHandlerTests
{
    private readonly Mock<IEvidenceAssetRepository> _assetsRepo = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly FakeDateTimeProvider _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _assetId = Guid.NewGuid();
    private readonly Guid _fileRecordId = Guid.NewGuid();

    public GetScreenshotUrlQueryHandlerTests()
    {
        _tenantContext.Setup(t => t.TenantId).Returns(_tenantId);
    }

    private GetScreenshotUrlQueryHandler CreateHandler() => new(
        _assetsRepo.Object,
        _fileStorage.Object,
        _tenantContext.Object,
        _clock);

    private MonitoringEvidenceAsset MakeAsset() => new()
    {
        Id = _assetId,
        TenantId = _tenantId,
        EmployeeId = Guid.NewGuid(),
        FileRecordId = _fileRecordId,
        CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    [Fact]
    public async Task Handle_AssetNotFound_Returns404()
    {
        _assetsRepo.Setup(r => r.GetByIdAsync(_tenantId, _assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringEvidenceAsset?)null);

        var result = await CreateHandler().Handle(new GetScreenshotUrlQuery(_assetId), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_FileRecordNotFound_Returns404()
    {
        _assetsRepo.Setup(r => r.GetByIdAsync(_tenantId, _assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeAsset());
        _fileStorage.Setup(s => s.GetSignedUrlAsync(_tenantId, _fileRecordId, TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Failure("file_record_not_found", 404));

        var result = await CreateHandler().Handle(new GetScreenshotUrlQuery(_assetId), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_HappyPath_DelegatesToFileStorageAndReturnsUrlWithExpiry()
    {
        const string signedUrl = "https://r2.example.com/shot.png?sig=abc&expires=xyz";

        _assetsRepo.Setup(r => r.GetByIdAsync(_tenantId, _assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeAsset());
        _fileStorage.Setup(s => s.GetSignedUrlAsync(_tenantId, _fileRecordId, TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success(signedUrl));

        var result = await CreateHandler().Handle(new GetScreenshotUrlQuery(_assetId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Url.Should().Be(signedUrl);
        result.Value.ExpiresAt.Should().BeCloseTo(
            _clock.UtcNow.Add(TimeSpan.FromMinutes(15)),
            precision: TimeSpan.FromSeconds(1));

        _fileStorage.Verify(
            s => s.GetSignedUrlAsync(_tenantId, _fileRecordId, TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
