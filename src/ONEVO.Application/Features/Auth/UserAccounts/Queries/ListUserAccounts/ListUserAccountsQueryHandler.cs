using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.UserAccounts.DTOs;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.UserAccounts.Queries.ListUserAccounts;

/// <summary>Every user account in the tenant for Settings &gt; Users (users:read).</summary>
public sealed class ListUserAccountsQueryHandler(
    IUserAccountRepository accounts,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ListUserAccountsQuery, Result<IReadOnlyList<UserAccountResponse>>>
{
    public async Task<Result<IReadOnlyList<UserAccountResponse>>> Handle(ListUserAccountsQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await accounts.ListAsync(currentUser.TenantId, now, ct);

        IReadOnlyList<UserAccountResponse> items = rows
            .Select(r => UserAccountStatus.ToResponse(r, currentUser.UserId, now))
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Result<IReadOnlyList<UserAccountResponse>>.Success(items);
    }
}
