using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.SuspendUserAccount;

public record SuspendUserAccountCommand(Guid UserId) : IRequest<Result>;
