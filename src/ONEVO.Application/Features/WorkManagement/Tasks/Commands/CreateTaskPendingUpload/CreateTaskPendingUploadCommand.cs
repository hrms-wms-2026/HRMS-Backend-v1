using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;

public sealed record CreateTaskPendingUploadCommand(
    string Purpose, string OriginalFileName, string ContentType, Stream Content
) : IRequest<Result<FileRecordDto>>;
