namespace ONEVO.Application.Features.AgentGateway.Policy;

public interface IEffectiveAgentPolicyResolver
{
    EffectiveAgentPolicy Resolve(
        string? rawPolicyJson,
        EffectiveAgentPolicyContext context);
}

