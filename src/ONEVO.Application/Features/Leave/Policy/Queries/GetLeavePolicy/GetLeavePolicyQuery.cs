using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;

public record GetLeavePolicyQuery(Guid LeavePolicyId) : IRequest<Result<LeavePolicyResponse>>;
