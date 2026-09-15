using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;

public sealed record DeleteTaskPendingUploadCommand(Guid FileId) : IRequest<Result>;
