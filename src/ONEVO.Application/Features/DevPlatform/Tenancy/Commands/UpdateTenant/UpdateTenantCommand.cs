using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateTenant;

public sealed record UpdateTenantCommand(
    Guid TenantId,
    string? Name,
    string? Slug,
    string? IndustryProfile) : IRequest<Result>;
