using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.LinkProjectLogo;

public sealed record LinkProjectLogoCommand(Guid ProjectId, Guid FileId) : IRequest<Result<ProjectLogoResponse>>;
