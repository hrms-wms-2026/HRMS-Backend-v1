using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CloneConfigurationTemplate;

public sealed class CloneConfigurationTemplateCommandHandler
    : IRequestHandler<CloneConfigurationTemplateCommand, Result<ConfigurationTemplateDto>>
{
    private readonly IConfigurationTemplateRepository _templates;
    private readonly IUnitOfWork _unitOfWork;

    public CloneConfigurationTemplateCommandHandler(
        IConfigurationTemplateRepository templates,
        IUnitOfWork unitOfWork)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ConfigurationTemplateDto>> Handle(
        CloneConfigurationTemplateCommand request,
        CancellationToken ct)
    {
        var source = await _templates.GetByIdAsync(request.TemplateId, ct);
        if (source is null)
        {
            return Result<ConfigurationTemplateDto>.NotFound("Configuration template not found.");
        }

        var newKey = await NextAvailableCloneKeyAsync(source.TemplateKey, ct);

        var clone = new ConfigurationTemplate
        {
            Id = Guid.NewGuid(),
            TemplateKey = newKey,
            TemplateType = source.TemplateType,
            Name = source.Name + " (Copy)",
            Description = source.Description,
            Version = 1,
            ModuleKeysJson = source.ModuleKeysJson,
            IndustryProfileTag = source.IndustryProfileTag,
            PayloadJson = source.PayloadJson,
            IsSystem = false,
            IsActive = true,
            CreatedById = request.CreatedById,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _templates.AddAsync(clone, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ConfigurationTemplateDto>.Success(ConfigurationTemplateMapper.ToDto(clone));
    }

    private async Task<string> NextAvailableCloneKeyAsync(string sourceKey, CancellationToken ct)
    {
        var candidate = sourceKey + "-copy";
        var suffix = 2;
        while (await _templates.GetByTemplateKeyAsync(candidate, ct) is not null)
        {
            candidate = $"{sourceKey}-copy-{suffix}";
            suffix += 1;
        }
        return candidate;
    }
}
