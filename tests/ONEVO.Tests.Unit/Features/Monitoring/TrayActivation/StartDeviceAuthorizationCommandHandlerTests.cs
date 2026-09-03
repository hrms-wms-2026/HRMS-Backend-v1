using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.StartDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

public class StartDeviceAuthorizationCommandHandlerTests
{
    private readonly Mock<ITrayActivationRepository> _repository = new();
    private readonly Mock<ITrayTokenService> _tokenService = new();
    private readonly Mock<IConfiguration> _configuration = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    public StartDeviceAuthorizationCommandHandlerTests()
    {
        _repository
            .Setup(r => r.CountRecentDeviceAuthorizationRequestsAsync(
                It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _tokenService.Setup(t => t.GenerateOpaqueToken(It.IsAny<int>())).Returns("device-code");
        _tokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns("hashed");
        _configuration.Setup(c => c["Urls:AppBaseUrl"]).Returns("https://localhost:4200");
    }

    private StartDeviceAuthorizationCommandHandler CreateHandler() => new(
        _repository.Object,
        _tokenService.Object,
        _clock,
        _configuration.Object,
        _uow);

    [Fact]
    public async Task Handle_HappyPath_PersistsTheAuthorizationRow()
    {
        TrayDeviceAuthorization? saved = null;
        _repository
            .Setup(r => r.AddDeviceAuthorizationAsync(It.IsAny<TrayDeviceAuthorization>(), It.IsAny<CancellationToken>()))
            .Callback<TrayDeviceAuthorization, CancellationToken>((a, _) => saved = a)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new StartDeviceAuthorizationCommand("Laptop", "Windows 11", "fingerprint-1", "1.0.0"), default);

        result.IsSuccess.Should().BeTrue();
        saved.Should().NotBeNull();
        // The bug this test guards against: AddDeviceAuthorizationAsync only stages the entity in
        // EF Core's change tracker — nothing reaches the database unless SaveChangesAsync is also
        // called. Without it, /start returns 200 with a well-formed response, but every
        // subsequent lookup (preview, approve, poll) 404s because the row was never persisted.
        _uow.SaveCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_RateLimited_DoesNotSave()
    {
        _repository
            .Setup(r => r.CountRecentDeviceAuthorizationRequestsAsync(
                It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        var result = await CreateHandler().Handle(
            new StartDeviceAuthorizationCommand("Laptop", "Windows 11", "fingerprint-1", "1.0.0"), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(429);
        _uow.SaveCallCount.Should().Be(0);
    }
}
