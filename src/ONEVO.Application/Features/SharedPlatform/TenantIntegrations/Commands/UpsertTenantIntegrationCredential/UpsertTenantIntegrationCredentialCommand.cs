using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertTenantIntegrationCredential;

public sealed record UpsertTenantIntegrationCredentialCommand(
    Guid TenantId, string IntegrationKey, string? AccessToken, string? RefreshToken,
    DateTimeOffset? TokenExpiresAt, string[] ScopesGranted,
    string? ExternalAccountId, string? ExternalAccountName,
    Guid ConnectedByUserId, string Status) : IRequest<Result<TenantIntegrationCredentialDto>>;

public sealed class UpsertTenantIntegrationCredentialCommandHandler
    : IRequestHandler<UpsertTenantIntegrationCredentialCommand, Result<TenantIntegrationCredentialDto>>
{
    private readonly ITenantIntegrationCredentialRepository _repository;
    private readonly IEncryptionService _encryption;

    public UpsertTenantIntegrationCredentialCommandHandler(
        ITenantIntegrationCredentialRepository repository,
        IEncryptionService encryption)
    {
        _repository = repository;
        _encryption = encryption;
    }

    public async Task<Result<TenantIntegrationCredentialDto>> Handle(
        UpsertTenantIntegrationCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var integrationKey = TenantIntegrationCredentialRules.NormalizeIntegrationKey(
            request.IntegrationKey);

        if (!TenantIntegrationCredentialRules.IsAllowedStatus(request.Status))
        {
            return Result<TenantIntegrationCredentialDto>.Failure("Unsupported integration credential status.");
        }

        if (!await _repository.TenantExistsAsync(request.TenantId, cancellationToken))
        {
            return Result<TenantIntegrationCredentialDto>.NotFound("Tenant was not found.");
        }

        var integration = await _repository.GetIntegrationAsync(integrationKey, cancellationToken);
        if (integration is null)
        {
            return Result<TenantIntegrationCredentialDto>.NotFound("Integration was not found.");
        }

        if (integration.ConnectionScope is not "tenant" and not "both")
        {
            return Result<TenantIntegrationCredentialDto>.Failure(
                "User-only integrations cannot use tenant credential storage.");
        }

        var credential = await _repository.GetByTenantAndIntegrationAsync(
            request.TenantId, integrationKey, cancellationToken);
        var isNew = credential is null;
        credential ??= new TenantIntegrationCredential
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            IntegrationKey = integrationKey,
            ConnectedAt = DateTimeOffset.UtcNow
        };

        credential.AccessTokenEncrypted = request.AccessToken is null
            ? null : _encryption.Encrypt(request.AccessToken);
        credential.RefreshTokenEncrypted = request.RefreshToken is null
            ? null : _encryption.Encrypt(request.RefreshToken);
        credential.TokenExpiresAt = request.TokenExpiresAt;
        credential.ScopesGranted = request.ScopesGranted;
        credential.ExternalAccountId = request.ExternalAccountId;
        credential.ExternalAccountName = request.ExternalAccountName;
        credential.ConnectedByUserId = request.ConnectedByUserId;
        credential.Status = request.Status;
        credential.ErrorMessage = null;

        if (request.Status == "connected")
        {
            credential.DisconnectedAt = null;
        }

        if (isNew)
        {
            await _repository.AddAsync(credential, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return Result<TenantIntegrationCredentialDto>.Success(
            TenantIntegrationCredentialMapper.ToSafeDto(credential));
    }
}
