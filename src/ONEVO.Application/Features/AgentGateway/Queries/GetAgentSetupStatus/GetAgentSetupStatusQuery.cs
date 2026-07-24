using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentSetupStatus;

public sealed record GetAgentSetupStatusQuery(Guid AgentId)
    : IRequest<Result<AgentSetupStatusDto>>;

public sealed record AgentSetupStatusDto(
    string WorkMode,
    bool LocationRequired,
    bool LocationReady,
    bool ReferenceRequired,
    bool ReferenceReady,
    string? RemoteProfileStatus,
    string SetupState);
