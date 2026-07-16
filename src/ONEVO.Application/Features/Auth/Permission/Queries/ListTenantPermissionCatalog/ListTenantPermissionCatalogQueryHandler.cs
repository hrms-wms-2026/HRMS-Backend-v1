using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Permission.Queries.ListTenantPermissionCatalog;

public sealed class ListTenantPermissionCatalogQueryHandler
    : IRequestHandler<ListTenantPermissionCatalogQuery, Result<PermissionCatalogDto>>
{
    private readonly ITenantPermissionCatalogService _catalog;
    private readonly ICurrentUser _currentUser;

    public ListTenantPermissionCatalogQueryHandler(
        ITenantPermissionCatalogService catalog,
        ICurrentUser currentUser)
    {
        _catalog = catalog;
        _currentUser = currentUser;
    }

    public async Task<Result<PermissionCatalogDto>> Handle(
        ListTenantPermissionCatalogQuery request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PermissionCatalogDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PermissionCatalogDto>.Forbidden("Tenant context missing.");

        var dto = await _catalog.GetCatalogAsync(tenantId, ct);
        return Result<PermissionCatalogDto>.Success(dto);
    }
}
