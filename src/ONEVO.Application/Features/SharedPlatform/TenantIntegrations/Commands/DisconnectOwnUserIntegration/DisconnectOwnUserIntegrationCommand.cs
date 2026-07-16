using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectOwnUserIntegration;

public sealed record DisconnectOwnUserIntegrationCommand(string IntegrationKey)
    : IRequest<Result<UserIntegrationConnectionDto>>;

public sealed class DisconnectOwnUserIntegrationCommandHandler
    : IRequestHandler<DisconnectOwnUserIntegrationCommand, Result<UserIntegrationConnectionDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserIntegrationConnectionRepository _repository;

    public DisconnectOwnUserIntegrationCommandHandler(
        ICurrentUser currentUser,
        IUserIntegrationConnectionRepository repository)
    {
        _currentUser = currentUser;
        _repository = repository;
    }

    public async Task<Result<UserIntegrationConnectionDto>> Handle(
        DisconnectOwnUserIntegrationCommand request,
        CancellationToken cancellationToken)
    {
        var integrationKey = UserIntegrationConnectionRules.NormalizeIntegrationKey(
            request.IntegrationKey);
        var connection = await _repository.GetActiveAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            integrationKey,
            cancellationToken);
        if (connection is null)
        {
            return Result<UserIntegrationConnectionDto>.Success(
                UserIntegrationConnectionMapper.Disconnected(integrationKey));
        }

        var now = DateTimeOffset.UtcNow;
        connection.Status = "disconnected";
        connection.DisconnectedAt = now;
        connection.UpdatedAt = now;
        connection.AccessTokenEncrypted = null;
        connection.RefreshTokenEncrypted = null;
        connection.TokenExpiresAt = null;

        await _repository.SaveChangesAsync(cancellationToken);
        return Result<UserIntegrationConnectionDto>.Success(
            UserIntegrationConnectionMapper.ToSafeDto(connection));
    }
}
