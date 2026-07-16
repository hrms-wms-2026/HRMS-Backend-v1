using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSessions;

public record RevokePlatformUserSessionsCommand(Guid UserId) : IRequest;
