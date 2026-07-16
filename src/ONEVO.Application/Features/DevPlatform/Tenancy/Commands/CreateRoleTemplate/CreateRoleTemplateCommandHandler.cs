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
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateRoleTemplate;

public sealed class CreateRoleTemplateCommandHandler
    : IRequestHandler<CreateRoleTemplateCommand, Result<RoleTemplateDto>>
{
    private readonly IRoleTemplateRepository _templates;
    private readonly IPermissionRepository _permissions;
    private readonly IModuleCatalogService _moduleCatalog;
    private readonly IUnitOfWork _unitOfWork;

    public CreateRoleTemplateCommandHandler(
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

    public async Task<Result<RoleTemplateDto>> Handle(CreateRoleTemplateCommand request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result<RoleTemplateDto>.Failure("Name is required.");

        if (name.Length > 100)
            return Result<RoleTemplateDto>.Failure("Name must be at most 100 characters.");

        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length > 255)
            return Result<RoleTemplateDto>.Failure("Description must be at most 255 characters.");

        if (await _templates.GetByNameAsync(name, ct) is not null)
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

        var template = new RoleTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            ModuleKeysJson = RoleTemplateJson.SerializeStringList(moduleKeys),
            PermissionCodesJson = RoleTemplateJson.SerializeStringList(permCodes),
            IsSystem = false,
            Version = 1,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _templates.AddAsync(template, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<RoleTemplateDto>.Success(RoleTemplateMapper.ToDto(template));
    }
}
