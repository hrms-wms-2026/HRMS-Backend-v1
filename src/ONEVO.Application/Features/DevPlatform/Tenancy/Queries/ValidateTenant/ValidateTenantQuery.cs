using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ValidateTenant;

public sealed record ValidateTenantQuery(
    string? Slug,
    string? CompanyName,
    string? EmailDomain,
    string? RegistrationNumber,
    string? Country) : IRequest<Result<TenantValidationResponseDto>>;
