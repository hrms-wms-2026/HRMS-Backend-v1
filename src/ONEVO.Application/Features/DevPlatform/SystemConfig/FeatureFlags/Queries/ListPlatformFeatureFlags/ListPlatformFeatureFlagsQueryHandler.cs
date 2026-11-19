using MediatR;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Queries.ListPlatformFeatureFlags;

public sealed class ListPlatformFeatureFlagsQueryHandler
    : IRequestHandler<ListPlatformFeatureFlagsQuery, IReadOnlyList<PlatformFeatureFlagDto>>
{
    private readonly IPlatformFeatureFlagRepository _repo;

    public ListPlatformFeatureFlagsQueryHandler(IPlatformFeatureFlagRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<PlatformFeatureFlagDto>> Handle(
        ListPlatformFeatureFlagsQuery request, CancellationToken ct)
    {
        var flags = await _repo.ListAsync(ct);
        return flags
            .OrderBy(f => f.Key)
            .Select(f => new PlatformFeatureFlagDto(f.Id, f.Key, f.Name, f.Description, f.IsEnabled, f.CreatedAt, f.UpdatedAt))
            .ToList();
    }
}
