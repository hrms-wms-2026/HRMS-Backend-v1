using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.Policy;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentPolicy;

public class GetAgentPolicyQueryHandler
    : IRequestHandler<GetAgentPolicyQuery, Result<EffectiveAgentPolicy>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IUserProfileRepository _profiles;
    private readonly IVerificationRepository _verification;
    private readonly IEffectiveAgentPolicyResolver _policyResolver;

    public GetAgentPolicyQueryHandler(
        IAgentGatewayRepository repo,
        ITimeAttendanceRepository attendance,
        IUserProfileRepository profiles,
        IVerificationRepository verification,
        IEffectiveAgentPolicyResolver policyResolver)
    {
        _repo = repo;
        _attendance = attendance;
        _profiles = profiles;
        _verification = verification;
        _policyResolver = policyResolver;
    }

    public async Task<Result<EffectiveAgentPolicy>> Handle(
        GetAgentPolicyQuery request,
        CancellationToken cancellationToken)
    {
        var agent = await _repo.GetAgentByIdAsync(
            request.AgentId,
            cancellationToken);
        if (agent is null)
            return Result<EffectiveAgentPolicy>.NotFound("Agent not found.");

        var policy = await _repo.GetPolicyByAgentIdAsync(request.AgentId, cancellationToken);
        if (policy is null)
            return Result<EffectiveAgentPolicy>.NotFound("Policy not found for agent.");

        var deviceApproved =
            string.Equals(agent.Status, "active", StringComparison.Ordinal) &&
            agent.EmployeeId.HasValue &&
            policy.TenantId == agent.TenantId &&
            policy.AgentId == agent.Id;

        var activeAgentSession = false;
        var activePresence = false;
        var monitoringDisclosureAccepted = false;

        if (deviceApproved)
        {
            var employeeId = agent.EmployeeId!.Value;
            var session = await _repo.GetActiveSessionByDeviceIdAsync(
                agent.DeviceId,
                cancellationToken);
            activeAgentSession =
                session is not null &&
                session.IsActive &&
                session.TenantId == agent.TenantId &&
                session.EmployeeId == employeeId &&
                string.Equals(
                    session.DeviceId,
                    agent.DeviceId,
                    StringComparison.Ordinal);

            var deviceSession = await _attendance.GetOpenDeviceSessionAsync(
                agent.Id,
                cancellationToken);
            activePresence =
                deviceSession is not null &&
                deviceSession.SessionEnd is null &&
                deviceSession.TenantId == agent.TenantId &&
                deviceSession.EmployeeId == employeeId &&
                deviceSession.DeviceId == agent.Id;

            var employee = await _profiles.GetEmployeeByIdAsync(
                employeeId,
                cancellationToken);
            if (employee is not null && employee.TenantId == agent.TenantId)
            {
                var consent = await _verification.GetLatestConsentAsync(
                    agent.TenantId,
                    employee.UserId,
                    "monitoring",
                    cancellationToken);
                monitoringDisclosureAccepted =
                    consent is not null &&
                    consent.TenantId == agent.TenantId &&
                    consent.UserId == employee.UserId &&
                    string.Equals(
                        consent.ConsentType,
                        "monitoring",
                        StringComparison.Ordinal) &&
                    consent.Consented;
            }
        }

        var effectivePolicy = _policyResolver.Resolve(
            policy.PolicyJson,
            new EffectiveAgentPolicyContext(
                DeviceApproved: deviceApproved,
                ActiveAgentSession: activeAgentSession,
                ActivePresence: activePresence,
                MonitoringDisclosureAccepted: monitoringDisclosureAccepted));

        return Result<EffectiveAgentPolicy>.Success(effectivePolicy);
    }
}
