using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectTenantIntegrationCredential;

public sealed record DisconnectTenantIntegrationCredentialCommand(Guid Id)
    : IRequest<Result<TenantIntegrationCredentialDto>>, IAuditedCommand<Result<TenantIntegrationCredentialDto>>
{
    public AuditEntry DescribeAudit(Result<TenantIntegrationCredentialDto>? response)
        => AuditEntry.For("tenant_integration.disconnected", "tenant_integration_credential", Id, response?.Value?.TenantId, new { IntegrationKey = response?.Value?.IntegrationKey });
}

public sealed class DisconnectTenantIntegrationCredentialCommandHandler
    : IRequestHandler<DisconnectTenantIntegrationCredentialCommand, Result<TenantIntegrationCredentialDto>>
{
    private readonly ITenantIntegrationCredentialRepository _repository;

    public DisconnectTenantIntegrationCredentialCommandHandler(ITenantIntegrationCredentialRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<TenantIntegrationCredentialDto>> Handle(
        DisconnectTenantIntegrationCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var credential = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (credential is null)
        {
            return Result<TenantIntegrationCredentialDto>.NotFound("Tenant integration credential was not found.");
        }

        credential.Status = "disconnected";
        credential.DisconnectedAt = DateTimeOffset.UtcNow;
        credential.AccessTokenEncrypted = null;
        credential.RefreshTokenEncrypted = null;
        credential.TokenExpiresAt = null;

        await _repository.SaveChangesAsync(cancellationToken);
        return Result<TenantIntegrationCredentialDto>.Success(
            TenantIntegrationCredentialMapper.ToSafeDto(credential));
    }
}
