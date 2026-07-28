namespace ONEVO.Application.Features.AgentGateway.Policy;

public sealed record EffectiveAgentPolicyContext(
    bool DeviceApproved,
    bool ActiveAgentSession,
    bool ActivePresence,
    bool MonitoringDisclosureAccepted);

