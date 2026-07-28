using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.RecordMonitoringConsent;

public sealed record RecordMonitoringConsentCommand(
    Guid AgentId,
    bool Consented,
    string NoticeVersion)
    : IRequest<Result<MonitoringConsentResponse>>;

public sealed record MonitoringConsentResponse(
    bool Consented,
    string NoticeVersion,
    DateTimeOffset RecordedAt);

