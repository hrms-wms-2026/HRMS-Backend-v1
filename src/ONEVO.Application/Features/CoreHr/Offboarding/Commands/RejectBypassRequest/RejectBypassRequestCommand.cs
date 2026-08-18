using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;

public sealed record RejectBypassRequestCommand(Guid BypassRequestId, string? DecisionComment) : IRequest<Result>;
