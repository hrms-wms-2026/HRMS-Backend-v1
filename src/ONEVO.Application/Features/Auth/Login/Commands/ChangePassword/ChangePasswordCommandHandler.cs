using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.ChangePassword;

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLogRepository _auditLog;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ChangePasswordCommandHandler(
        IUserRepository users, IPasswordHasher passwordHasher, IAuditLogRepository auditLog,
        IOutboxWriter outbox, IUnitOfWork unitOfWork, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _auditLog = auditLog;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var user = await _users.GetByIdAsync(_currentUser.UserId, ct);
        if (user is null)
            return Result.NotFound("User not found.");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Forbidden("Current password is incorrect.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.UpdatedAt = _clock.UtcNow;

        await _auditLog.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            UserId = _currentUser.UserId,
            Action = "user.password_changed",
            ResourceType = "User",
            ResourceId = _currentUser.UserId,
            CreatedAt = _clock.UtcNow
        }, ct);

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.EmployeeSecurityUpdated,
            new { UserId = _currentUser.UserId, Event = "password_changed", ChangedAt = _clock.UtcNow },
            tenantId: _currentUser.TenantId, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
