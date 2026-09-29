using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.LinkMyAvatar;

public sealed record LinkMyAvatarCommand(Guid FileId) : IRequest<Result<Guid?>>;
