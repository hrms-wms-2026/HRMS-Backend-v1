using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.Storage.File.Queries.GetFile;

public sealed record GetFileQuery(Guid FileId) : IRequest<Result<FileStreamDto>>;
