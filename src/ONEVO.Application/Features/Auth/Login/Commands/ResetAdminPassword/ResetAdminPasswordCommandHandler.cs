using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.ResetAdminPassword;

/// <summary>
/// No MFA field, no MFA verification here - MFA enrolment is untouched. Sessions are revoked
/// and the next sign-in re-enforces the existing, already-correct AdminLoginCommandHandler MFA
/// gate. Every failure path returns the same generic error literal - never a distinct message
/// that would let a caller distinguish invalid/expired/used-token from a missing credential.
/// </summary>
public sealed class ResetAdminPasswordCommandHandler : IRequestHandler<ResetAdminPasswordCommand, Result>
{
    private const string GenericTokenError = "Invalid or expired reset token.";

    private readonly IPlatformUserRepository _platformUsers;
    private readonly IPlatformUserCredentialRepository _credentials;
    private readonly IPlatformUserSessionRepository _sessions;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public ResetAdminPasswordCommandHandler(
        IPlatformUserRepository platformUsers,
        IPlatformUserCredentialRepository credentials,
        IPlatformUserSessionRepository sessions,
        IPlatformAuthEventRepository authEvents,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokens,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _platformUsers = platformUsers;
        _credentials = credentials;
        _sessions = sessions;
        _authEvents = authEvents;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _outbox = outbox;
        _clock = clock;
        _uow = uow;
    }

    public Task<Result> Handle(ResetAdminPasswordCommand request, CancellationToken ct)
    {
        return _uow.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = _clock.UtcNow;
            var tokenHash = _tokens.HashToken(request.Token);

            var userId = await _credentials.TryConsumeResetTokenAsync(tokenHash, now, innerCt);
            if (userId is null)
                return Result.Failure(GenericTokenError, 400);

            var credential = await _credentials.GetActivePasswordCredentialAsync(userId.Value, innerCt);
            if (credential is null)
                return Result.Failure(GenericTokenError, 400);

            var user = await _platformUsers.GetByIdAsync(userId.Value, innerCt);
            if (user is null)
                return Result.Failure(GenericTokenError, 400);

            credential.PasswordHash = _passwordHasher.Hash(request.NewPassword);
            credential.UpdatedAt = now;
            _credentials.Update(credential);

            await _sessions.RevokeAllByUserIdAsync(userId.Value, innerCt);

            await _outbox.EnqueueAsync(
                OutboxMessageTypes.AdminPasswordChangedEmail,
                new AdminPasswordChangedEmailPayload(user.Email),
                tenantId: null,
                innerCt);

            await _authEvents.AddAsync(new PlatformAuthEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                EventType = PlatformAuthEvent.AdminPasswordResetCompleted,
                MetadataJson = "{}",
                CreatedAt = now
            }, innerCt);

            await _uow.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
