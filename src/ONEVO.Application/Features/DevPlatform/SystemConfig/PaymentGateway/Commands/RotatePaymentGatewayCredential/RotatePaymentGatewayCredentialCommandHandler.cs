using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.RotatePaymentGatewayCredential;

/// <summary>
/// Rotates (replaces) credentials for an existing gateway config.
/// A new encrypted credential version row is created and the prior active row is deactivated.
/// Credential secrets are encrypted before persistence and never returned by any GET response.
/// </summary>
public sealed record RotatePaymentGatewayCredentialCommand(
    Guid GatewayConfigId,
    /// <summary>Plain-text new secret — encrypted immediately; never stored raw.</summary>
    string NewSecretKey,
    /// <summary>Plain-text new webhook secret — encrypted immediately; nullable.</summary>
    string? NewWebhookSecret,
    Guid ActorPlatformUserId) : IRequest<Result<Unit>>;

public sealed class RotatePaymentGatewayCredentialCommandHandler
    : IRequestHandler<RotatePaymentGatewayCredentialCommand, Result<Unit>>
{
    private readonly IPaymentGatewayRepository _repo;
    private readonly IEncryptionService _encryption;

    public RotatePaymentGatewayCredentialCommandHandler(
        IPaymentGatewayRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<Result<Unit>> Handle(
        RotatePaymentGatewayCredentialCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Verify config exists
        var config = await _repo.GetByIdAsync(request.GatewayConfigId, false, false, cancellationToken);
        if (config is null)
            return Result<Unit>.NotFound("Payment gateway config not found.");

        // 2. Deactivate current active credential
        var current = await _repo.GetActiveCredentialAsync(request.GatewayConfigId, cancellationToken);
        if (current is not null)
        {
            current.IsActive = false;
            current.DeactivatedById = request.ActorPlatformUserId;
            current.DeactivatedAt = DateTimeOffset.UtcNow;
        }

        // 3. Calculate next version number
        var maxVersion = await _repo.GetMaxCredentialVersionAsync(request.GatewayConfigId, cancellationToken);

        // 4. Encrypt new credentials
        var secretEncrypted = _encryption.EncryptBytes(request.NewSecretKey);
        byte[]? webhookSecretEncrypted = request.NewWebhookSecret is not null
            ? _encryption.EncryptBytes(request.NewWebhookSecret)
            : null;

        // 5. Create new active version row
        var newCredential = new PaymentGatewayCredential
        {
            Id = Guid.NewGuid(),
            PaymentGatewayConfigId = request.GatewayConfigId,
            SecretEncrypted = secretEncrypted,
            WebhookSecretEncrypted = webhookSecretEncrypted,
            EncryptionKeyVersion = "v1",
            CredentialVersion = maxVersion + 1,
            IsActive = true,
            RotatedById = request.ActorPlatformUserId,
            RotatedAt = DateTimeOffset.UtcNow
        };

        await _repo.AddCredentialAsync(newCredential, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
