using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectBanner;

public record GetProjectBannerQuery(Guid ProjectId) : IRequest<Result<FileStreamDto>>;
