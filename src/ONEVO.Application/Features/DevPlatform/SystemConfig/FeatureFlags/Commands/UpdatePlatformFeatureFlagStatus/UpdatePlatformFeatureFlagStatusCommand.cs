using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Commands.UpdatePlatformFeatureFlagStatus;

public record UpdatePlatformFeatureFlagStatusCommand(Guid FlagId, bool IsEnabled)
    : IRequest<Result<PlatformFeatureFlagDto>>;
