using System.Text.RegularExpressions;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.FeatureFlags.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Commands.CreatePlatformFeatureFlag;

public sealed partial class CreatePlatformFeatureFlagCommandHandler
    : IRequestHandler<CreatePlatformFeatureFlagCommand, Result<PlatformFeatureFlagDto>>
{
    private readonly IPlatformFeatureFlagRepository _repo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreatePlatformFeatureFlagCommandHandler(
        IPlatformFeatureFlagRepository repo, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PlatformFeatureFlagDto>> Handle(
        CreatePlatformFeatureFlagCommand request, CancellationToken ct)
    {
        var key = request.Key.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(key) || !KeyPattern().IsMatch(key))
            return Result<PlatformFeatureFlagDto>.Failure(
                "Key is required and must be lowercase letters, numbers, and underscores only.");

        if (string.IsNullOrWhiteSpace(name))
            return Result<PlatformFeatureFlagDto>.Failure("Name is required.");

        if (await _repo.ExistsByKeyAsync(key, ct))
            return Result<PlatformFeatureFlagDto>.Conflict($"A feature flag with key '{key}' already exists.");

        var now = _clock.UtcNow;
        var flag = new PlatformFeatureFlag
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = request.Description?.Trim(),
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
        };

        await _repo.AddAsync(flag, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<PlatformFeatureFlagDto>.Success(
            new PlatformFeatureFlagDto(flag.Id, flag.Key, flag.Name, flag.Description, flag.IsEnabled, flag.CreatedAt, flag.UpdatedAt));
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex KeyPattern();
}
