using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.SuspendUserAccount;

/// <summary>
/// Blocks an active account from signing in (users:manage) and ends all of its sessions. Sets
/// IsActive = false so every existing login/session check rejects it without change, and records
/// SuspendedAt so the account can be told apart from a pending invite or an offboarded user.
/// You cannot suspend yourself or an Owner, so a tenant can never lock itself out.
/// </summary>
public sealed class SuspendUserAccountCommandHandler(
    IUserRepository users,
    IUserAccountRepository accounts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<SuspendUserAccountCommand, Result>
{
    public async Task<Result> Handle(SuspendUserAccountCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var now = clock.UtcNow;

        var account = await accounts.GetAsync(tenantId, request.UserId, now, ct);
        var user = account is null ? null : await users.GetByIdAsync(request.UserId, ct);
        if (account is null || user is null || user.TenantId != tenantId)
            return Result.NotFound("The user account could not be found.");

        if (request.UserId == currentUser.UserId)
            return Result.Conflict("You can't suspend your own account.");
        if (account.HoldsOwnerRole)
            return Result.Forbidden("The Owner account can't be suspended.");
        if (UserAccountStatus.Of(account, now) != UserAccountStatus.Active)
            return Result.Conflict("Only active accounts can be suspended.");

        user.IsActive = false;
        user.SuspendedAt = now;
        user.SuspendedById = currentUser.UserId;
        user.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(ct);
        await accounts.RevokeAllAccessAsync(tenantId, user.Id, now, "user_suspended", ct);

        return Result.Success();
    }
}
