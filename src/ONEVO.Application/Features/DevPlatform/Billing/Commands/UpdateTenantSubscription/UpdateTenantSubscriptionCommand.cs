using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateTenantSubscription;

public sealed record UpdateTenantSubscriptionCommand(
    Guid TenantId,
    int? IncludedSeats,
    bool? OverageAllowed,
    bool? IsTest) : IRequest<Result<TenantSubscriptionDetailDto>>;
