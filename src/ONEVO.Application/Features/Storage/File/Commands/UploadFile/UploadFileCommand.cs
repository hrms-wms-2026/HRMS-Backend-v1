using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.Storage.File.Commands.UploadFile;

public sealed record UploadFileCommand(
    string Purpose,
    string FileName,
    string ContentType,
    Stream Content) : IRequest<Result<FileRecordDto>>;
