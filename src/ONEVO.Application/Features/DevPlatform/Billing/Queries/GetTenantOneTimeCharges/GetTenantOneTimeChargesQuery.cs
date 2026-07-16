using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantOneTimeCharges;

public sealed record GetTenantOneTimeChargesQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<TenantOneTimeChargeDto>>>;
