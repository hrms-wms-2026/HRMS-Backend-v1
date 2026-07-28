using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.RequestPasswordReset;

public class RequestPasswordResetCommandHandler : IRequestHandler<RequestPasswordResetCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordResetTokenRepository _passwordResetTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISecureTokenGenerator _tokenService;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContext _tenantContext;

    public RequestPasswordResetCommandHandler(
        IUserRepository users,
        IPasswordResetTokenRepository passwordResetTokens,
        IUnitOfWork unitOfWork,
        ISecureTokenGenerator tokenService,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        ITenantContext tenantContext)
    {
        _users = users;
        _passwordResetTokens = passwordResetTokens;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _outbox = outbox;
        _clock = clock;
        _tenantContext = tenantContext;
    }

    public async Task<Result> Handle(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        // Silently succeed if context is not a resolved tenant - avoids enumeration
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Result.Success();

        // A resolved tenant context without a slug is malformed - fail closed rather than
        // enqueue a reset email whose link would resolve to the base host (EmailTemplateRenderer
        // only builds a tenant-host reset URL when a slug is supplied). No token is created and
        // no repository/outbox call is made, so this is indistinguishable from an unknown user.
        if (string.IsNullOrWhiteSpace(_tenantContext.Slug))
            return Result.Success();

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByTenantAndEmailAsync(_tenantContext.TenantId, email, cancellationToken);

        // Treat inactive users the same as not found - avoid enumeration
        if (user is not null && !user.IsActive)
            user = null;

        // Always return success to prevent email enumeration
        if (user is null)
            return Result.Success();

        // Invalidate existing reset tokens
        var existing = await _passwordResetTokens.ListValidByUserIdAsync(user.Id, _clock.UtcNow, cancellationToken);

        foreach (var t in existing)
            t.UsedAt = _clock.UtcNow; // Mark as used/invalidated

        var rawToken = _tokenService.GenerateOpaqueToken(); // Random opaque token
        var tokenHash = _tokenService.HashToken(rawToken);

        await _passwordResetTokens.AddAsync(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = user.TenantId,
            TokenHash = tokenHash,
            ExpiresAt = _clock.UtcNow.AddHours(1),
            CreatedAt = _clock.UtcNow
        }, cancellationToken);

        // Enqueue the reset email in the same unit of work as the token: one SaveChangesAsync
        // commits both atomically, so a token can never exist without a durable, retryable email
        // job (and vice versa). The outbox processor retries delivery on a transient provider
        // failure; the client only ever sees the caller's generic 200 either way.
        await _outbox.EnqueueAsync(
            OutboxMessageTypes.PasswordResetEmail,
            new PasswordResetEmailPayload(user.TenantId, user.Id, user.Email, rawToken, _tenantContext.Slug),
            tenantId: user.TenantId,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
