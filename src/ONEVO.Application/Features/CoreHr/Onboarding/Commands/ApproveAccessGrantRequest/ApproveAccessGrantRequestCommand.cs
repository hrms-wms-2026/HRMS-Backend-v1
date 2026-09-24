using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;

public record ApproveAccessGrantRequestCommand(Guid AccessGrantRequestId) : IRequest<Result<ApproveAccessGrantRequestResponse>>;
