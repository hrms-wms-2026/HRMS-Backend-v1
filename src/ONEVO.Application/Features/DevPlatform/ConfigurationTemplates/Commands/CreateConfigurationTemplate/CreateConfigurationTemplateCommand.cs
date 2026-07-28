using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CreateConfigurationTemplate;

public sealed record CreateConfigurationTemplateCommand(
    string TemplateKey,
    string TemplateType,
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    string? IndustryProfileTag,
    JsonElement PayloadJson,
    bool IsSystem,
    Guid CreatedById) : IRequest<Result<ConfigurationTemplateDto>>;
