using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateOneTimeCharge;

public sealed record CreateOneTimeChargeCommand(
    Guid TenantId,
    string SetupOptionKey,
    string Description,
    decimal Amount,
    string Currency = "USD")
    : IRequest<Result<TenantOneTimeChargeDto>>;
