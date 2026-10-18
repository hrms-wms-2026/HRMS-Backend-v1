using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.ReactivateUserAccount;

/// <summary>
/// Undoes a suspension (users:manage). Only a suspended account qualifies - this must never turn
/// an offboarded user or an unaccepted invite back on, which also have IsActive = false.
/// </summary>
public sealed class ReactivateUserAccountCommandHandler(
    IUserRepository users,
    IUserAccountRepository accounts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ReactivateUserAccountCommand, Result>
{
    public async Task<Result> Handle(ReactivateUserAccountCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var now = clock.UtcNow;

        var account = await accounts.GetAsync(tenantId, request.UserId, now, ct);
        var user = account is null ? null : await users.GetByIdAsync(request.UserId, ct);
        if (account is null || user is null || user.TenantId != tenantId)
            return Result.NotFound("The user account could not be found.");

        if (UserAccountStatus.Of(account, now) != UserAccountStatus.Suspended)
            return Result.Conflict("Only suspended accounts can be reactivated.");

        user.IsActive = true;
        user.SuspendedAt = null;
        user.SuspendedById = null;
        user.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
