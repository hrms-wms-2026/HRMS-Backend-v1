using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RemoveMyAvatar;

public sealed record RemoveMyAvatarCommand : IRequest<Result>;
