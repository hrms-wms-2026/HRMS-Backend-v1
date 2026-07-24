using System.Net;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.CaptureSetupLocation;
using ONEVO.Application.Features.AgentGateway.Location;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class CaptureSetupLocationCommandHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _agents = new();
    private readonly Mock<IUserProfileRepository> _profiles = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IVerificationRepository> _verification = new();
    private readonly Mock<ILocationVerificationService> _locations = new();
    private readonly Mock<IRequestNetworkContext> _network = new();
    private readonly Mock<INetworkEvidenceHasher> _hasher = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _agentId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);

    private CaptureSetupLocationCommandHandler CreateHandler() =>
        new(
            _agents.Object,
            _profiles.Object,
            _legalEntities.Object,
            _verification.Object,
            _locations.Object,
            _network.Object,
            _hasher.Object,
            _clock.Object,
            _uow.Object);

    private void ArrangeActiveAgent(string workMode = "onsite")
    {
        _clock.SetupGet(clock => clock.UtcNow).Returns(_now);
        _network.SetupGet(context => context.ClientIp).Returns(IPAddress.Parse("203.0.113.10"));
        _agents.Setup(repository => repository.GetAgentByIdAsync(
                _agentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredAgent
            {
                Id = _agentId,
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                Status = "active"
            });
        _profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                _employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee
            {
                Id = _employeeId,
                TenantId = _tenantId,
                LegalEntityId = _legalEntityId
            });
        _profiles.Setup(repository => repository.GetWorkLocationSettingsAsync(
                _employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeWorkLocationSettings
            {
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                WorkMode = workMode,
                WorkLocationVerificationEnabled = true
            });
        _legalEntities.Setup(repository => repository.GetByIdAsync(
                _legalEntityId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity
            {
                Id = _legalEntityId,
                TenantId = _tenantId,
                OfficeLatitude = 6.9271m,
                OfficeLongitude = 79.8612m,
                OfficeAllowedRadiusMeters = 150
            });
        _hasher.Setup(service => service.Protect(_tenantId, "aabbccddeeff00112233445566778899"))
            .Returns("server-wifi-hash");
        _hasher.Setup(service => service.Protect(_tenantId, "00112233445566778899aabbccddeeff"))
            .Returns("server-gateway-hash");
        _uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CaptureSetupLocationCommand Command() =>
        new(
            _agentId,
            new LocationCapture(6.9271m, 79.8612m, 12m, _now, "granted"),
            "private_ipv4",
            "aabbccddeeff00112233445566778899",
            "00112233445566778899aabbccddeeff",
            false);

    [Fact]
    public async Task Handle_OnsiteMatch_PersistsProtectedEvidence()
    {
        ArrangeActiveAgent();
        _locations.Setup(service => service.Evaluate(
                It.IsAny<LocationCapture>(),
                It.IsAny<LocationTarget>(),
                _now))
            .Returns(new LocationMatchResult(true, true, 5m, string.Empty));

        AgentWorkLocationEvidence? saved = null;
        _agents.Setup(repository => repository.AddWorkLocationEvidenceAsync(
                It.IsAny<AgentWorkLocationEvidence>(),
                It.IsAny<CancellationToken>()))
            .Callback<AgentWorkLocationEvidence, CancellationToken>((value, _) => saved = value)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("matched", result.Value!.MatchState);
        Assert.NotNull(saved);
        Assert.Equal(IPAddress.Parse("203.0.113.10"), saved.PublicIp);
        Assert.Equal("server-wifi-hash", saved.WifiBssidHash);
        Assert.Equal("server-gateway-hash", saved.GatewayMacHash);
        Assert.DoesNotContain("aabbccddeeff", saved.WifiBssidHash);
    }

    [Fact]
    public async Task Handle_OnsiteMismatch_PersistsMismatchWithoutApprovingLocation()
    {
        ArrangeActiveAgent();
        _locations.Setup(service => service.Evaluate(
                It.IsAny<LocationCapture>(),
                It.IsAny<LocationTarget>(),
                _now))
            .Returns(new LocationMatchResult(true, false, 900m, "outside_allowed_radius"));

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("mismatch", result.Value!.MatchState);
        Assert.Equal("outside_allowed_radius", result.Value.FailureCode);
        _agents.Verify(repository => repository.AddWorkLocationEvidenceAsync(
            It.Is<AgentWorkLocationEvidence>(value => value.MatchStatus == "mismatch"),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Handle_FirstRemoteCapture_CreatesActiveProfileWhenReferenceIsNotBlocking()
    {
        ArrangeActiveAgent("remote");
        _locations.Setup(service => service.ValidateCapture(It.IsAny<LocationCapture>(), _now))
            .Returns(new LocationMatchResult(true, true, 0m, string.Empty));
        _verification.Setup(repository => repository.GetActiveRemoteProfileAsync(
                _employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeRemoteWorkProfile?)null);
        _verification.Setup(repository => repository.GetActivePolicyAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationPolicy
            {
                TenantId = _tenantId,
                IsActive = true,
                BlockMonitoringUntilReferenceApproved = false
            });

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("active", result.Value!.RemoteProfileState);
        _verification.Verify(repository => repository.AddRemoteProfileAsync(
            It.Is<EmployeeRemoteWorkProfile>(profile => profile.Status == "active"),
            It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("capture_stale")]
    [InlineData("permission_denied")]
    public async Task Handle_InvalidCapture_FailsClosed(string failureCode)
    {
        ArrangeActiveAgent("remote");
        _locations.Setup(service => service.ValidateCapture(It.IsAny<LocationCapture>(), _now))
            .Returns(new LocationMatchResult(false, false, null, failureCode));

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(failureCode, result.Error);
        _agents.Verify(repository => repository.AddWorkLocationEvidenceAsync(
            It.IsAny<AgentWorkLocationEvidence>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveAgent_IsForbidden()
    {
        _agents.Setup(repository => repository.GetAgentByIdAsync(
                _agentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredAgent
            {
                Id = _agentId,
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                Status = "revoked"
            });

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _agents.Verify(repository => repository.AddWorkLocationEvidenceAsync(
            It.IsAny<AgentWorkLocationEvidence>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
