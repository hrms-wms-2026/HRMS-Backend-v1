using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.UpdateConfigurationTemplateMetadata;

public sealed record UpdateConfigurationTemplateMetadataCommand(
    Guid TemplateId,
    string? Name,
    string? Description,
    IReadOnlyList<string>? ModuleKeys,
    string? IndustryProfileTag,
    JsonElement? PayloadJson) : IRequest<Result<ConfigurationTemplateDto>>;
