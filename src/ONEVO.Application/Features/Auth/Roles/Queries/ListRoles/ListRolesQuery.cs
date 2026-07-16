using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Roles.Queries.ListRoles;

public record ListRolesQuery() : IRequest<Result<IReadOnlyList<RoleSummaryDto>>>;
