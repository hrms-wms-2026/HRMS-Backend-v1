using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;

public sealed record AchieveSprintCommand(Guid SprintId) : IRequest<Result<SprintResponse>>;
