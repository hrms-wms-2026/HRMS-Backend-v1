using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;

public sealed record UpsertOwnUserIntegrationConnectionCommand(
    string IntegrationKey,
    string? ProviderUserId,
    string? ProviderUsername,
    string? ProviderEmail,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? TokenExpiresAt,
    string[] ScopesGranted)
    : IRequest<Result<UserIntegrationConnectionDto>>;

public sealed class UpsertOwnUserIntegrationConnectionCommandHandler
    : IRequestHandler<UpsertOwnUserIntegrationConnectionCommand, Result<UserIntegrationConnectionDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserIntegrationConnectionRepository _repository;
    private readonly IEncryptionService _encryption;

    public UpsertOwnUserIntegrationConnectionCommandHandler(
        ICurrentUser currentUser,
        IUserIntegrationConnectionRepository repository,
        IEncryptionService encryption)
    {
        _currentUser = currentUser;
        _repository = repository;
        _encryption = encryption;
    }

    public async Task<Result<UserIntegrationConnectionDto>> Handle(
        UpsertOwnUserIntegrationConnectionCommand request,
        CancellationToken cancellationToken)
    {
        var integrationKey = UserIntegrationConnectionRules.NormalizeIntegrationKey(
            request.IntegrationKey);
        if (string.IsNullOrWhiteSpace(integrationKey))
        {
            return Result<UserIntegrationConnectionDto>.Failure("Integration key is required.");
        }

        var connection = await _repository.GetActiveAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            integrationKey,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var isNew = connection is null;

        connection ??= new UserIntegrationConnection
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            UserId = _currentUser.UserId,
            IntegrationKey = integrationKey,
            CreatedAt = now
        };

        connection.ProviderUserId = request.ProviderUserId;
        connection.ProviderUsername = request.ProviderUsername;
        connection.ProviderEmail = request.ProviderEmail;
        connection.AccessTokenEncrypted = EncryptIfPresent(request.AccessToken);
        connection.RefreshTokenEncrypted = EncryptIfPresent(request.RefreshToken);
        connection.TokenExpiresAt = request.TokenExpiresAt;
        connection.ScopesGranted = request.ScopesGranted;
        connection.Status = "connected";
        connection.ErrorMessage = null;
        connection.ConnectedAt = now;
        connection.DisconnectedAt = null;
        connection.UpdatedAt = isNew ? null : now;

        if (isNew)
        {
            await _repository.AddAsync(connection, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return Result<UserIntegrationConnectionDto>.Success(
            UserIntegrationConnectionMapper.ToSafeDto(connection));
    }

    private string? EncryptIfPresent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return _encryption.Encrypt(value);
    }
}
