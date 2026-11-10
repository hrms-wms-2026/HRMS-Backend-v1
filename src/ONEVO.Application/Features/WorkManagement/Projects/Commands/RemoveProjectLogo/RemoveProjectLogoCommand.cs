using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.RemoveProjectLogo;

public sealed record RemoveProjectLogoCommand(Guid ProjectId) : IRequest<Result>;
