using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Storage.File.Commands.DeleteFile;

public sealed record DeleteFileCommand(Guid FileId) : IRequest<Result>;
