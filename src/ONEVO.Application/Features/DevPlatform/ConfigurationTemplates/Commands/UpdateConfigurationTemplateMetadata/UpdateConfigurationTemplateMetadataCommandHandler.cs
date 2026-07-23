using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.UpdateConfigurationTemplateMetadata;

public sealed class UpdateConfigurationTemplateMetadataCommandHandler
    : IRequestHandler<UpdateConfigurationTemplateMetadataCommand, Result<ConfigurationTemplateDto>>
{
    private readonly IConfigurationTemplateRepository _templates;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateConfigurationTemplateMetadataCommandHandler(
        IConfigurationTemplateRepository templates,
        IUnitOfWork unitOfWork)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ConfigurationTemplateDto>> Handle(
        UpdateConfigurationTemplateMetadataCommand request,
        CancellationToken ct)
    {
        var template = await _templates.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
        {
            return Result<ConfigurationTemplateDto>.NotFound("Configuration template not found.");
        }

        if (template.IsSystem)
        {
            return Result<ConfigurationTemplateDto>.Failure("System templates cannot be edited directly. Clone it instead.");
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
            {
                return Result<ConfigurationTemplateDto>.Failure("name must be at most 150 characters.");
            }
            template.Name = name;
        }

        if (request.Description is not null)
        {
            if (request.Description.Length > 500)
            {
                return Result<ConfigurationTemplateDto>.Failure("description must be at most 500 characters.");
            }
            template.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (request.ModuleKeys is not null)
        {
            var moduleKeys = request.ModuleKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            template.ModuleKeysJson = ConfigurationTemplateMapper.SerializeStringList(moduleKeys);
        }

        if (request.IndustryProfileTag is not null)
        {
            template.IndustryProfileTag = string.IsNullOrWhiteSpace(request.IndustryProfileTag)
                ? null
                : request.IndustryProfileTag.Trim();
        }

        if (request.PayloadJson is not null)
        {
            if (request.PayloadJson.Value.ValueKind != JsonValueKind.Object)
            {
                return Result<ConfigurationTemplateDto>.Failure("payload_json must be a JSON object.");
            }
            template.PayloadJson = request.PayloadJson.Value.GetRawText();
        }

        template.Version += 1;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ConfigurationTemplateDto>.Success(ConfigurationTemplateMapper.ToDto(template));
    }
}
