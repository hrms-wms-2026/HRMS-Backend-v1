using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetTenantPermissionCatalog;

public sealed record GetTenantPermissionCatalogQuery(Guid TenantId)
    : IRequest<Result<PermissionCatalogDto>>;
