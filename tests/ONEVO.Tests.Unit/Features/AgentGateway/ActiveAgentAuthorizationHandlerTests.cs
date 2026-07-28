using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Moq;
using ONEVO.Api.Authorization;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class ActiveAgentAuthorizationHandlerTests
{
    [Fact]
    public async Task ActiveAgent_WithMatchingTenantAndEmployeeBinding_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = Guid.NewGuid(),
            Status = "active"
        };

        Assert.True(await AuthorizeAsync(agent, agent.Id, tenantId));
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("revoked")]
    public async Task NonActiveAgent_IsDenied(string status)
    {
        var tenantId = Guid.NewGuid();
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = Guid.NewGuid(),
            Status = status
        };

        Assert.False(await AuthorizeAsync(agent, agent.Id, tenantId));
    }

    [Fact]
    public async Task ActiveAgent_FromDifferentTenant_IsDenied()
    {
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Status = "active"
        };

        Assert.False(await AuthorizeAsync(agent, agent.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task ActiveAgent_WithoutEmployeeBinding_IsDenied()
    {
        var tenantId = Guid.NewGuid();
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = null,
            Status = "active"
        };

        Assert.False(await AuthorizeAsync(agent, agent.Id, tenantId));
    }

    [Fact]
    public async Task ActiveAgent_WithoutActiveEmployeeSession_IsDenied()
    {
        var tenantId = Guid.NewGuid();
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = Guid.NewGuid(),
            DeviceId = "employee-device",
            Status = "active"
        };

        Assert.False(await AuthorizeAsync(
            agent,
            agent.Id,
            tenantId,
            includeActiveSession: false));
    }

    [Fact]
    public async Task ActiveAgent_WithSessionForDifferentEmployee_IsDenied()
    {
        var tenantId = Guid.NewGuid();
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = Guid.NewGuid(),
            DeviceId = "employee-device",
            Status = "active"
        };
        var session = new AgentSession
        {
            TenantId = tenantId,
            DeviceId = agent.DeviceId,
            EmployeeId = Guid.NewGuid(),
            IsActive = true
        };

        Assert.False(await AuthorizeAsync(agent, agent.Id, tenantId, session));
    }

    private static async Task<bool> AuthorizeAsync(
        RegisteredAgent? agent,
        Guid claimAgentId,
        Guid claimTenantId,
        AgentSession? activeSession = null,
        bool includeActiveSession = true)
    {
        var repo = new Mock<IAgentGatewayRepository>();
        repo.Setup(r => r.GetAgentByIdAsync(claimAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        if (agent is not null)
        {
            activeSession ??= includeActiveSession && agent.EmployeeId.HasValue
                ? new AgentSession
                {
                    TenantId = agent.TenantId,
                    DeviceId = agent.DeviceId,
                    EmployeeId = agent.EmployeeId.Value,
                    IsActive = true
                }
                : null;
            repo.Setup(r => r.GetActiveSessionByDeviceIdAsync(
                    agent.DeviceId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeSession);
        }

        var requirement = new ActiveAgentRequirement();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", claimAgentId.ToString()),
            new Claim("tenant_id", claimTenantId.ToString()),
            new Claim("type", "agent")
        ], "AgentScheme"));
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await new ActiveAgentAuthorizationHandler(repo.Object).HandleAsync(context);
        return context.HasSucceeded;
    }
}
