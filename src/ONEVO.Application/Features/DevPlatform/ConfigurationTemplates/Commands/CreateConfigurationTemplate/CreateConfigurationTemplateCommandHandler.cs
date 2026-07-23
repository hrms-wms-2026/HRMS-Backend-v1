using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CreateConfigurationTemplate;

public sealed class CreateConfigurationTemplateCommandHandler
    : IRequestHandler<CreateConfigurationTemplateCommand, Result<ConfigurationTemplateDto>>
{
    private readonly IConfigurationTemplateRepository _templates;
    private readonly IUnitOfWork _unitOfWork;

    public CreateConfigurationTemplateCommandHandler(
        IConfigurationTemplateRepository templates,
        IUnitOfWork unitOfWork)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ConfigurationTemplateDto>> Handle(
        CreateConfigurationTemplateCommand request,
        CancellationToken ct)
    {
        var templateKey = request.TemplateKey.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(templateKey) || templateKey.Length > 100)
        {
            return Result<ConfigurationTemplateDto>.Failure("template_key is required and must be at most 100 characters.");
        }

        if (!ConfigurationTemplate.AllTypes.Contains(request.TemplateType))
        {
            return Result<ConfigurationTemplateDto>.Failure(
                $"template_type must be one of: {string.Join(", ", ConfigurationTemplate.AllTypes)}.");
        }

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
        {
            return Result<ConfigurationTemplateDto>.Failure("name is required and must be at most 150 characters.");
        }

        if (request.Description is { Length: > 500 })
        {
            return Result<ConfigurationTemplateDto>.Failure("description must be at most 500 characters.");
        }

        if (request.PayloadJson.ValueKind != JsonValueKind.Object)
        {
            return Result<ConfigurationTemplateDto>.Failure("payload_json must be a JSON object.");
        }

        if (await _templates.GetByTemplateKeyAsync(templateKey, ct) is not null)
        {
            return Result<ConfigurationTemplateDto>.Conflict($"template_key '{templateKey}' is already in use.");
        }

        var moduleKeys = request.ModuleKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var template = new ConfigurationTemplate
        {
            Id = Guid.NewGuid(),
            TemplateKey = templateKey,
            TemplateType = request.TemplateType,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Version = 1,
            ModuleKeysJson = ConfigurationTemplateMapper.SerializeStringList(moduleKeys),
            IndustryProfileTag = string.IsNullOrWhiteSpace(request.IndustryProfileTag) ? null : request.IndustryProfileTag.Trim(),
            PayloadJson = request.PayloadJson.GetRawText(),
            IsSystem = request.IsSystem,
            IsActive = true,
            CreatedById = request.CreatedById,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _templates.AddAsync(template, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ConfigurationTemplateDto>.Success(ConfigurationTemplateMapper.ToDto(template));
    }
}
