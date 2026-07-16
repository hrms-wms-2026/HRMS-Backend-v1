using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetTenantById;

public sealed record GetTenantByIdQuery(Guid TenantId) : IRequest<Result<TenantDetailDto>>;
