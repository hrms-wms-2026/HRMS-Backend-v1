using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Roles.Queries.GetRoleById;

public record GetRoleByIdQuery(Guid RoleId) : IRequest<Result<RoleDetailDto>>;
