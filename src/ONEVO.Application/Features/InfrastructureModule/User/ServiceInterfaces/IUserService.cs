using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.InfrastructureModule.User.Commands.CreateUser;
using ONEVO.Application.Features.InfrastructureModule.User.DTOs.Responses;

namespace ONEVO.Application.Features.InfrastructureModule.User.ServiceInterfaces;

public interface IUserService
{
    Task<Result<UserDto>> CreateUserAsync(CreateUserCommand command, CancellationToken ct);
    Task<Result<UserDto>> GetUserByIdAsync(Guid userId, CancellationToken ct);
}
