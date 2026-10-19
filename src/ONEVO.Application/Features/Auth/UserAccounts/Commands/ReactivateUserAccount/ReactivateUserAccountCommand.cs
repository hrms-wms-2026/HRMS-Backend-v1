using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.ReactivateUserAccount;

public record ReactivateUserAccountCommand(Guid UserId) : IRequest<Result>;
