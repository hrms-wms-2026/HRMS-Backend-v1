using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantSubscription;

public sealed record GetTenantSubscriptionQuery(Guid TenantId)
    : IRequest<Result<TenantSubscriptionDetailDto>>;
