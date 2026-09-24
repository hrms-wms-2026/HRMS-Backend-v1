using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectLogo;

public record GetProjectLogoQuery(Guid ProjectId) : IRequest<Result<FileStreamDto>>;
