using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.RequestAdminPasswordReset;

/// <summary>
/// Always returns success regardless of whether the email matches a real, active admin
/// with a password credential - enumeration-safe by design. Mirrors the tenant
/// RequestPasswordResetCommandHandler shape, scoped to PlatformUser/PlatformUserCredential.
/// </summary>
public sealed class RequestAdminPasswordResetCommandHandler
    : IRequestHandler<RequestAdminPasswordResetCommand, Result>
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private readonly IPlatformUserRepository _platformUsers;
    private readonly IPlatformUserCredentialRepository _credentials;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RequestAdminPasswordResetCommandHandler(
        IPlatformUserRepository platformUsers,
        IPlatformUserCredentialRepository credentials,
        IPlatformAuthEventRepository authEvents,
        ISecureTokenGenerator tokens,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _platformUsers = platformUsers;
        _credentials = credentials;
        _authEvents = authEvents;
        _tokens = tokens;
        _outbox = outbox;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(RequestAdminPasswordResetCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _platformUsers.GetByEmailAsync(email, ct);
        if (user is null || user.Status != PlatformUser.StatusActive)
            return Result.Success();

        var credential = await _credentials.GetActivePasswordCredentialAsync(user.Id, ct);
        if (credential is null)
            return Result.Success();

        var now = _clock.UtcNow;
        var rawToken = _tokens.GenerateOpaqueToken();
        credential.ResetTokenHash = _tokens.HashToken(rawToken);
        credential.ResetTokenExpiresAt = now.Add(TokenLifetime);
        credential.UpdatedAt = now;
        _credentials.Update(credential);

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.AdminPasswordResetEmail,
            new AdminPasswordResetEmailPayload(user.Email, rawToken),
            tenantId: null,
            ct);

        await _authEvents.AddAsync(new PlatformAuthEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            EventType = PlatformAuthEvent.AdminPasswordResetRequested,
            SourceIp = request.IpAddress,
            UserAgent = request.UserAgent,
            MetadataJson = "{}",
            CreatedAt = now
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
