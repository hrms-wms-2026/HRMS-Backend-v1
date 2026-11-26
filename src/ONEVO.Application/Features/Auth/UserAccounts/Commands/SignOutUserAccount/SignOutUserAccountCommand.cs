using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.SignOutUserAccount;

public record SignOutUserAccountCommand(Guid UserId)
    : IRequest<Result<SignOutUserAccountResponse>>, IAuditedCommand<Result<SignOutUserAccountResponse>>
{
    public AuditEntry DescribeAudit(Result<SignOutUserAccountResponse>? response)
        => AuditEntry.For("user.signed_out", "user", UserId,
            details: new { endedCount = response?.Value?.EndedCount });
}

public record SignOutUserAccountResponse(int EndedCount);
