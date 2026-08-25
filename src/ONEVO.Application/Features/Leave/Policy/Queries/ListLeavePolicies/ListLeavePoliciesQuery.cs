using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;

public record ListLeavePoliciesQuery(bool IncludeInactive) : IRequest<Result<IReadOnlyList<LeavePolicyListItemResponse>>>;
