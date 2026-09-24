using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;

public sealed record AchieveProjectCommand(Guid ProjectId) : IRequest<Result>;
