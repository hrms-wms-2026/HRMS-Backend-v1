using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSession;

public record RevokePlatformUserSessionCommand(Guid UserId, Guid SessionId) : IRequest;
