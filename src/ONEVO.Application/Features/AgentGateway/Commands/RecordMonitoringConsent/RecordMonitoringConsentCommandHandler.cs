using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.RecordMonitoringConsent;

public sealed class RecordMonitoringConsentCommandHandler
    : IRequestHandler<
        RecordMonitoringConsentCommand,
        Result<MonitoringConsentResponse>>
{
    private readonly IAgentGatewayRepository _agents;
    private readonly IUserProfileRepository _profiles;
    private readonly IVerificationRepository _verification;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RecordMonitoringConsentCommandHandler(
        IAgentGatewayRepository agents,
        IUserProfileRepository profiles,
        IVerificationRepository verification,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _agents = agents;
        _profiles = profiles;
        _verification = verification;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<MonitoringConsentResponse>> Handle(
        RecordMonitoringConsentCommand request,
        CancellationToken cancellationToken)
    {
        var noticeVersion = request.NoticeVersion.Trim();
        if (noticeVersion.Length is < 1 or > 50)
        {
            return Result<MonitoringConsentResponse>.Failure(
                "A valid monitoring notice version is required.",
                400);
        }

        var agent = await _agents.GetAgentByIdAsync(
            request.AgentId,
            cancellationToken);
        if (agent is null ||
            agent.EmployeeId is null ||
            !string.Equals(agent.Status, "active", StringComparison.Ordinal))
        {
            return Result<MonitoringConsentResponse>.Forbidden(
                "Agent is not an approved active device.");
        }

        var employee = await _profiles.GetEmployeeByIdAsync(
            agent.EmployeeId.Value,
            cancellationToken);
        if (employee is null || employee.TenantId != agent.TenantId)
            return Result<MonitoringConsentResponse>.NotFound("Employee not found.");

        var now = _clock.UtcNow;
        await _verification.AddConsentAsync(new GdprConsentRecord
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            UserId = employee.UserId,
            ConsentType = "monitoring",
            Consented = request.Consented,
            ConsentedAt = now,
            NoticeVersion = noticeVersion,
            CapturedAgentId = agent.Id
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<MonitoringConsentResponse>.Success(
            new MonitoringConsentResponse(
                request.Consented,
                noticeVersion,
                now));
    }
}

