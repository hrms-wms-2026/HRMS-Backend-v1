using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;

public record SetMyAvatarCommand(string FileName, string ContentType, Stream Content) : IRequest<Result<Guid?>>;
