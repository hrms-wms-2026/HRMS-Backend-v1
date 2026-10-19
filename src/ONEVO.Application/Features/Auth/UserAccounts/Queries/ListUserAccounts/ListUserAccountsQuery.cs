using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.UserAccounts.DTOs;

namespace ONEVO.Application.Features.Auth.UserAccounts.Queries.ListUserAccounts;

public record ListUserAccountsQuery : IRequest<Result<IReadOnlyList<UserAccountResponse>>>;
