using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.SignOutUserAccount;

public record SignOutUserAccountCommand(Guid UserId) : IRequest<Result<SignOutUserAccountResponse>>;

public record SignOutUserAccountResponse(int EndedCount);
