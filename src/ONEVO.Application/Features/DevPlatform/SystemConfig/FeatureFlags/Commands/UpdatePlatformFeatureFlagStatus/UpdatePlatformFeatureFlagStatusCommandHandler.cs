using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Commands.UpdatePlatformFeatureFlagStatus;

public sealed class UpdatePlatformFeatureFlagStatusCommandHandler
    : IRequestHandler<UpdatePlatformFeatureFlagStatusCommand, Result<PlatformFeatureFlagDto>>
{
    private readonly IPlatformFeatureFlagRepository _repo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdatePlatformFeatureFlagStatusCommandHandler(
        IPlatformFeatureFlagRepository repo, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PlatformFeatureFlagDto>> Handle(
        UpdatePlatformFeatureFlagStatusCommand request, CancellationToken ct)
    {
        var flag = await _repo.GetByIdAsync(request.FlagId, ct);
        if (flag is null)
            return Result<PlatformFeatureFlagDto>.NotFound($"Feature flag '{request.FlagId}' not found.");

        flag.IsEnabled = request.IsEnabled;
        flag.UpdatedAt = _clock.UtcNow;
        _repo.UpdateFlag(flag);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<PlatformFeatureFlagDto>.Success(
            new PlatformFeatureFlagDto(flag.Id, flag.Key, flag.Name, flag.Description, flag.IsEnabled, flag.CreatedAt, flag.UpdatedAt));
    }
}
