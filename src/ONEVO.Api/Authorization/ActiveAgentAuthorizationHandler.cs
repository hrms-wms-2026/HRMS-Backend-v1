using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Api.Authorization;

public sealed class ActiveAgentAuthorizationHandler
    : AuthorizationHandler<ActiveAgentRequirement>
{
    private readonly IAgentGatewayRepository _repo;

    public ActiveAgentAuthorizationHandler(IAgentGatewayRepository repo) => _repo = repo;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveAgentRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var agentId) ||
            !Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var tenantId))
        {
            return;
        }

        var agent = await _repo.GetAgentByIdAsync(agentId, CancellationToken.None);
        if (agent is not null &&
            agent.TenantId == tenantId &&
            agent.EmployeeId.HasValue &&
            string.Equals(agent.Status, "active", StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }
    }
}
