using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;

public sealed record GetTaskFileQuery(Guid FileId) : IRequest<Result<FileStreamDto>>;
