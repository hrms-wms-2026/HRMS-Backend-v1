using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.RecordConsentEvent;

public record RecordConsentEventCommand(
    Guid TenantId,
    Guid AgentDeviceId,
    Guid IncidentId,
    string Decision,
    DateTimeOffset OccurredAt) : IRequest<Result>;
