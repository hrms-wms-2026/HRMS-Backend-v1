using MediatR;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.Queries;

public class GetModuleCatalogPermissionsQueryHandler : IRequestHandler<GetModuleCatalogPermissionsQuery, Result<IReadOnlyList<ModulePermissionOwnershipDto>>>
{
    private readonly IModuleCatalogRepository _repo;

    public GetModuleCatalogPermissionsQueryHandler(IModuleCatalogRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<IReadOnlyList<ModulePermissionOwnershipDto>>> Handle(GetModuleCatalogPermissionsQuery request, CancellationToken cancellationToken)
    {
        var module = await _repo.GetByKeyAsync(request.ModuleKey, cancellationToken);

        if (module is null)
        {
            return Result<IReadOnlyList<ModulePermissionOwnershipDto>>.NotFound($"Module '{request.ModuleKey}' not found.");
        }

        var permissions = module.PermissionOwnerships
            .Select(p => new ModulePermissionOwnershipDto(
                p.PermissionCode,
                p.IsDefaultPermission))
            .OrderBy(p => p.PermissionCode)
            .ToList();

        return Result<IReadOnlyList<ModulePermissionOwnershipDto>>.Success(permissions);
    }
}
