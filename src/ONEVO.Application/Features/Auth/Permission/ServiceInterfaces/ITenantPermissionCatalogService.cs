using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

public interface ITenantPermissionCatalogService
{
    Task<PermissionCatalogDto> GetCatalogAsync(Guid tenantId, CancellationToken ct = default);
}
