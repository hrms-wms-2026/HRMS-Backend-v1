using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;

public class DisableMfaCommandHandler : IRequestHandler<DisableMfaCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserMfaRepository _userMfa;
    private readonly IAuditLogRepository _auditLog;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public DisableMfaCommandHandler(
        IUserRepository users, IPasswordHasher passwordHasher, IUserMfaRepository userMfa,
        IAuditLogRepository auditLog, IOutboxWriter outbox, IUnitOfWork unitOfWork,
        ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _userMfa = userMfa;
        _auditLog = auditLog;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(DisableMfaCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var user = await _users.GetByIdAsync(_currentUser.UserId, ct);
        if (user is null)
            return Result.NotFound("User not found.");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Forbidden("Current password is incorrect.");

        // Remove both the verified (active) registration and any stale unverified/in-progress
        // setup - GetTotpAsync's isVerified parameter is not nullable, so both states are checked
        // explicitly rather than assumed. Matches EnableMfaCommandHandler's own "remove existing
        // unverified setup before creating a new one" precedent.
        var verified = await _userMfa.GetTotpAsync(user.Id, isVerified: true, ct);
        if (verified is not null)
            _userMfa.Remove(verified);

        var unverified = await _userMfa.GetTotpAsync(user.Id, isVerified: false, ct);
        if (unverified is not null)
            _userMfa.Remove(unverified);

        if (verified is null && unverified is null)
            return Result.Conflict("MFA is not currently enabled.");

        await _auditLog.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            UserId = _currentUser.UserId,
            Action = "user.mfa_disabled",
            ResourceType = "User",
            ResourceId = _currentUser.UserId,
            CreatedAt = _clock.UtcNow
        }, ct);

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.EmployeeSecurityUpdated,
            new { UserId = _currentUser.UserId, Event = "mfa_disabled", ChangedAt = _clock.UtcNow },
            tenantId: _currentUser.TenantId, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
