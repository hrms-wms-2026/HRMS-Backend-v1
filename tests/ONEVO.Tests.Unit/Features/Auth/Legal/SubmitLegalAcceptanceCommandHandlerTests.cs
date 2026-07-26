using FluentAssertions;
using Moq;

using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Legal;

public sealed class SubmitLegalAcceptanceCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ILegalDocumentVersionRepository> _versionRepository = new();
    private readonly Mock<ILegalAcceptanceRepository> _acceptanceRepository = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public SubmitLegalAcceptanceCommandHandlerTests()
    {
        _tenantContext.SetupGet(x => x.IsResolved).Returns(true);
        _tenantContext.SetupGet(x => x.ContextMode).Returns(TenantContextMode.Tenant);
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);

        _currentUser.SetupGet(x => x.UserId).Returns(_userId);

        _clock.SetupGet(x => x.UtcNow).Returns(_now);
    }

    [Fact]
    public async Task RejectsNonRequiredDocument()
    {
        var docVer = BuildVersion(isRequired: false, status: "published", publishedAt: _now.AddDays(-1));
        SetupVersion(docVer);

        var result = await Handler().Handle(SubmitCommand(docVer), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _acceptanceRepository.Verify(x => x.AddAsync(It.IsAny<LegalAcceptanceRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectsPublishedDocumentWithNullPublishedAt()
    {
        var docVer = BuildVersion(isRequired: true, status: "published", publishedAt: null);
        SetupVersion(docVer);

        var result = await Handler().Handle(SubmitCommand(docVer), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _acceptanceRepository.Verify(x => x.AddAsync(It.IsAny<LegalAcceptanceRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectsFuturePublishedAt()
    {
        var docVer = BuildVersion(isRequired: true, status: "published", publishedAt: _now.AddDays(1));
        SetupVersion(docVer);

        var result = await Handler().Handle(SubmitCommand(docVer), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _acceptanceRepository.Verify(x => x.AddAsync(It.IsAny<LegalAcceptanceRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("archived")]
    public async Task RejectsNonPublishedStatus(string status)
    {
        var docVer = BuildVersion(isRequired: true, status: status, publishedAt: _now.AddDays(-1));
        SetupVersion(docVer);

        var result = await Handler().Handle(SubmitCommand(docVer), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _acceptanceRepository.Verify(x => x.AddAsync(It.IsAny<LegalAcceptanceRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectsUnknownDocument()
    {
        _versionRepository
            .Setup(x => x.GetByDocumentTypeAndVersionAsync("terms", "9.9", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var command = new SubmitLegalAcceptanceCommand(
            [new LegalAcceptanceItemInput("terms", "9.9", "accepted")],
            IpAddress: null,
            UserAgent: null);

        var result = await Handler().Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RejectsInvalidDecision()
    {
        var docVer = BuildVersion(isRequired: true, status: "published", publishedAt: _now.AddDays(-1));
        SetupVersion(docVer);

        var command = new SubmitLegalAcceptanceCommand(
            [new LegalAcceptanceItemInput(docVer.DocumentType, docVer.Version, "maybe")],
            IpAddress: null,
            UserAgent: null);

        var result = await Handler().Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _versionRepository.Verify(
            x => x.GetByDocumentTypeAndVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AcceptsValidRequiredPublishedDocument_AndBindsTenantAndUserFromContextOnly()
    {
        var docVer = BuildVersion(isRequired: true, status: "published", publishedAt: _now.AddDays(-1));
        SetupVersion(docVer);

        LegalAcceptanceRecord? capturedRecord = null;
        _acceptanceRepository
            .Setup(x => x.AddAsync(It.IsAny<LegalAcceptanceRecord>(), It.IsAny<CancellationToken>()))
            .Callback<LegalAcceptanceRecord, CancellationToken>((record, _) => capturedRecord = record)
            .Returns(Task.CompletedTask);

        var result = await Handler().Handle(SubmitCommand(docVer), default);

        result.IsSuccess.Should().BeTrue();
        capturedRecord.Should().NotBeNull();
        capturedRecord!.TenantId.Should().Be(_tenantId);
        capturedRecord.UserId.Should().Be(_userId);
        _acceptanceRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static LegalDocumentVersion BuildVersion(bool isRequired, string status, DateTimeOffset? publishedAt)
    {
        return new LegalDocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentType = "terms",
            Version = "1.0",
            Title = "Terms v1.0",
            IsRequired = isRequired,
            BlockScope = "dashboard",
            Status = status,
            PublishedAt = publishedAt
        };
    }

    private void SetupVersion(LegalDocumentVersion docVer)
    {
        _versionRepository
            .Setup(x => x.GetByDocumentTypeAndVersionAsync(docVer.DocumentType, docVer.Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(docVer);
    }

    private static SubmitLegalAcceptanceCommand SubmitCommand(LegalDocumentVersion docVer)
    {
        return new SubmitLegalAcceptanceCommand(
            [new LegalAcceptanceItemInput(docVer.DocumentType, docVer.Version, "accepted")],
            IpAddress: "127.0.0.1",
            UserAgent: "test-agent");
    }

    private SubmitLegalAcceptanceCommandHandler Handler()
    {
        return new SubmitLegalAcceptanceCommandHandler(
            _versionRepository.Object,
            _acceptanceRepository.Object,
            _tenantContext.Object,
            _currentUser.Object,
            _clock.Object);
    }
}
