using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateOneTimeCharge;

public sealed record UpdateOneTimeChargeCommand(
    Guid TenantId,
    Guid ChargeId,
    decimal? Amount,
    string? Status)
    : IRequest<Result<TenantOneTimeChargeDto>>;
