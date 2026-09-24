using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;

public record RejectAccessGrantRequestCommand(Guid AccessGrantRequestId, string? DecisionNote)
    : IRequest<Result<RejectAccessGrantRequestResponse>>;
