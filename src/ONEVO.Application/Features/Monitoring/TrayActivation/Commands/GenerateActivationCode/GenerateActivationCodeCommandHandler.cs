using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.GenerateActivationCode;

public class GenerateActivationCodeCommandHandler
    : IRequestHandler<GenerateActivationCodeCommand, Result<ActivationCodeResponseDto>>
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private const int MaxCodesPerHour = 3;
    private const int CodeExpiresInSeconds = 600;

    private readonly ITrayActivationRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public GenerateActivationCodeCommandHandler(
        ITrayActivationRepository repository,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ActivationCodeResponseDto>> Handle(
        GenerateActivationCodeCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var tenantId = _currentUser.TenantId;
        var now = _clock.UtcNow;

        if (_currentUser.LegalEntityId is not Guid legalEntityId)
            return Result<ActivationCodeResponseDto>.UnprocessableEntity(
                "Select an active company before creating a tray activation code.");

        var recentCount = await _repository.CountRecentCodesForUserAsync(
            userId, tenantId, now.AddHours(-1), cancellationToken);

        if (recentCount >= MaxCodesPerHour)
            return Result<ActivationCodeResponseDto>.Failure(
                "Too many activation codes requested. Please wait before trying again.", 429);

        var rawCode = GenerateCode();
        var codeHash = HashCode(rawCode);

        var activationCode = new TrayActivationCode
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            LegalEntityId = legalEntityId,
            CodeHash = codeHash,
            ExpiresAt = now.Add(CodeLifetime),
            CreatedAt = now
        };

        await _repository.AddActivationCodeAsync(activationCode, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ActivationCodeResponseDto>.Success(
            new ActivationCodeResponseDto(rawCode, CodeExpiresInSeconds));
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = new byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private static string HashCode(string rawCode)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawCode));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
