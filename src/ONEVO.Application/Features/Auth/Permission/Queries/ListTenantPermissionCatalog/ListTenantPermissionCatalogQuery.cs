using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Permission.Queries.ListTenantPermissionCatalog;

public sealed record ListTenantPermissionCatalogQuery
    : IRequest<Result<PermissionCatalogDto>>;
