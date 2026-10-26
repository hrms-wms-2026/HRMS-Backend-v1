using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetChecklistTemplateFile;

public sealed record GetChecklistTemplateFileQuery(Guid TemplateId, Guid FileId) : IRequest<Result<FileStreamDto>>;
