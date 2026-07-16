using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetProvisioningSummary;

public sealed record GetProvisioningSummaryQuery(Guid TenantId)
    : IRequest<Result<ProvisioningSummaryDto>>;
