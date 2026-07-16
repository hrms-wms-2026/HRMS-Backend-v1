using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetTenantPermissionCatalog;

public sealed class GetTenantPermissionCatalogQueryHandler
    : IRequestHandler<GetTenantPermissionCatalogQuery, Result<PermissionCatalogDto>>
{
    private readonly ITenantPermissionCatalogService _catalog;

    public GetTenantPermissionCatalogQueryHandler(ITenantPermissionCatalogService catalog)
        => _catalog = catalog;

    public async Task<Result<PermissionCatalogDto>> Handle(
        GetTenantPermissionCatalogQuery request,
        CancellationToken ct)
    {
        if (request.TenantId == Guid.Empty)
            return Result<PermissionCatalogDto>.Failure("Tenant id is required.", 400);

        var dto = await _catalog.GetCatalogAsync(request.TenantId, ct);
        return Result<PermissionCatalogDto>.Success(dto);
    }
}
