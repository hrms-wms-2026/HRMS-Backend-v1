using Moq;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentSetupStatus;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class GetAgentSetupStatusQueryHandlerTests
{
    [Fact]
    public async Task Handle_MatchedLocationAndApprovedReference_ReturnsReady()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var agents = new Mock<IAgentGatewayRepository>();
        var profiles = new Mock<IUserProfileRepository>();
        var verification = new Mock<IVerificationRepository>();

        agents.Setup(repository => repository.GetAgentByIdAsync(
                agentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredAgent
            {
                Id = agentId,
                TenantId = tenantId,
                EmployeeId = employeeId,
                Status = "active"
            });
        agents.Setup(repository => repository.GetLatestWorkLocationEvidenceAsync(
                agentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentWorkLocationEvidence
            {
                TenantId = tenantId,
                AgentId = agentId,
                EmployeeId = employeeId,
                MatchStatus = "matched"
            });
        profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, TenantId = tenantId });
        profiles.Setup(repository => repository.GetWorkLocationSettingsAsync(
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeWorkLocationSettings
            {
                TenantId = tenantId,
                EmployeeId = employeeId,
                WorkMode = "onsite",
                WorkLocationVerificationEnabled = true
            });
        verification.Setup(repository => repository.GetActivePolicyAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationPolicy
            {
                IsActive = true,
                RequirePhotoClockIn = true
            });
        verification.Setup(repository => repository.GetActiveReferencePhotoAsync(
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationReferencePhoto
            {
                TenantId = tenantId,
                EmployeeId = employeeId,
                Status = "approved",
                IsActive = true
            });

        var handler = new GetAgentSetupStatusQueryHandler(
            agents.Object,
            profiles.Object,
            verification.Object);
        var result = await handler.Handle(
            new GetAgentSetupStatusQuery(agentId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ready", result.Value!.SetupState);
        Assert.True(result.Value.LocationReady);
        Assert.True(result.Value.ReferenceReady);
    }
}
