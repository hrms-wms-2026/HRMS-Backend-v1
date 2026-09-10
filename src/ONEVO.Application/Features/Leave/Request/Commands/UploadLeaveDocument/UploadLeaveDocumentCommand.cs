using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Request.Commands.UploadLeaveDocument;

public record UploadLeaveDocumentCommand(string FileName, string ContentType, Stream Content)
    : IRequest<Result<FileRecordDto>>;
