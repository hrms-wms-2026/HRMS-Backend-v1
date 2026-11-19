using MediatR;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Queries.ListPlatformFeatureFlags;

public record ListPlatformFeatureFlagsQuery : IRequest<IReadOnlyList<PlatformFeatureFlagDto>>;
