using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Roles.Commands.ArchiveRole;

public record ArchiveRoleCommand(Guid RoleId) : IRequest<Result>;
