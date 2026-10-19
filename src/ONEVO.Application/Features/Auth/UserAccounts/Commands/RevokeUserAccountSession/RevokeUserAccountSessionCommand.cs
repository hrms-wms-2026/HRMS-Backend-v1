using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.RevokeUserAccountSession;

/// <summary>Ends one web session (SessionId) or disconnects one tray app (DeviceId) of a user.</summary>
public record RevokeUserAccountSessionCommand(Guid UserId, Guid? SessionId, Guid? DeviceId) : IRequest<Result>;
