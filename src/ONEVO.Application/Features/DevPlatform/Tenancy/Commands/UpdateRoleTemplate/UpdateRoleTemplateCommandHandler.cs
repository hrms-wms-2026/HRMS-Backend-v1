using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy;
using ONEVO.Application.Features.DevPlatform.Tenancy.Mappers;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateRoleTemplate;

public sealed class UpdateRoleTemplateCommandHandler
    : IRequestHandler<UpdateRoleTemplateCommand, Result<RoleTemplateDto>>
{
    private readonly IRoleTemplateRepository _templates;
    private readonly IPermissionRepository _permissions;
    private readonly IModuleCatalogService _moduleCatalog;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateRoleTemplateCommandHandler(
        IRoleTemplateRepository templates,
        IPermissionRepository permissions,
        IModuleCatalogService moduleCatalog,
        IUnitOfWork unitOfWork)
    {
        _templates = templates;
        _permissions = permissions;
        _moduleCatalog = moduleCatalog;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RoleTemplateDto>> Handle(UpdateRoleTemplateCommand request, CancellationToken ct)
    {
        var template = await _templates.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
            return Result<RoleTemplateDto>.NotFound("Role template not found.");

        if (template.IsSystem)
            return Result<RoleTemplateDto>.Forbidden("System role templates cannot be modified.");

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result<RoleTemplateDto>.Failure("Name is required.");

        if (name.Length > 100)
            return Result<RoleTemplateDto>.Failure("Name must be at most 100 characters.");

        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length > 255)
            return Result<RoleTemplateDto>.Failure("Description must be at most 255 characters.");

        var other = await _templates.GetByNameAsync(name, ct);
        if (other is not null && other.Id != template.Id)
            return Result<RoleTemplateDto>.Conflict($"A role template named '{name}' already exists.");

        var moduleKeys = request.ModuleKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var catalogKeys = await _moduleCatalog.GetActiveModuleKeysAsync(ct);
        var catalogSet = catalogKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownModules = moduleKeys.Where(k => !catalogSet.Contains(k)).OrderBy(k => k).ToList();
        if (unknownModules.Count > 0)
            return Result<RoleTemplateDto>.Failure(
                $"Unknown module keys: {string.Join(", ", unknownModules)}.");

        var permCodes = request.PermissionCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var resolved = await _permissions.GetByCodesAsync(permCodes, ct);
        var permError = RoleTemplateValidation.ValidatePermissionCodeList(permCodes, resolved);
        if (permError is not null)
            return Result<RoleTemplateDto>.Failure(permError);

        var newModulesJson = RoleTemplateJson.SerializeStringList(moduleKeys);
        var newPermsJson = RoleTemplateJson.SerializeStringList(permCodes);

        var contentChanged =
            !string.Equals(template.Name, name, StringComparison.Ordinal)
            || !string.Equals(template.Description, description, StringComparison.Ordinal)
            || template.ModuleKeysJson != newModulesJson
            || template.PermissionCodesJson != newPermsJson
            || template.IsActive != request.IsActive;

        template.Name = name;
        template.Description = description;
        template.ModuleKeysJson = newModulesJson;
        template.PermissionCodesJson = newPermsJson;
        template.IsActive = request.IsActive;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        if (contentChanged)
            template.Version++;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<RoleTemplateDto>.Success(RoleTemplateMapper.ToDto(template));
    }
}
