using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.IngestBatch;

public record IngestBatchCommand(
    Guid AgentId,
    Guid TenantId,
    string PayloadJson) : IRequest<Result>;
