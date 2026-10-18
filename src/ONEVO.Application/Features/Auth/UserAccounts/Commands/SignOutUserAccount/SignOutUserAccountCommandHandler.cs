using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.SignOutUserAccount;

/// <summary>
/// Signs a user out everywhere (users:manage) without suspending them - they can sign in again.
/// Not offered for your own account; use the normal sign-out for that.
/// </summary>
public sealed class SignOutUserAccountCommandHandler(
    IUserAccountRepository accounts,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<SignOutUserAccountCommand, Result<SignOutUserAccountResponse>>
{
    public async Task<Result<SignOutUserAccountResponse>> Handle(SignOutUserAccountCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var now = clock.UtcNow;

        if (request.UserId == currentUser.UserId)
            return Result<SignOutUserAccountResponse>.Conflict("Use Sign out to end your own session.");

        var account = await accounts.GetAsync(tenantId, request.UserId, now, ct);
        if (account is null)
            return Result<SignOutUserAccountResponse>.NotFound("The user account could not be found.");

        var ended = await accounts.RevokeAllAccessAsync(tenantId, request.UserId, now, "signed_out_by_admin", ct);
        return Result<SignOutUserAccountResponse>.Success(new SignOutUserAccountResponse(ended));
    }
}
