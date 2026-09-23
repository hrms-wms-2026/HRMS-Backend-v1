using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;

public sealed record ApproveBypassRequestCommand(Guid BypassRequestId) : IRequest<Result>;
