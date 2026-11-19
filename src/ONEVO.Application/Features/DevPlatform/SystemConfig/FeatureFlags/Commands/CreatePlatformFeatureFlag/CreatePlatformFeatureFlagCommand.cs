using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Commands.CreatePlatformFeatureFlag;

public record CreatePlatformFeatureFlagCommand(string Key, string Name, string? Description, bool IsEnabled)
    : IRequest<Result<PlatformFeatureFlagDto>>;
