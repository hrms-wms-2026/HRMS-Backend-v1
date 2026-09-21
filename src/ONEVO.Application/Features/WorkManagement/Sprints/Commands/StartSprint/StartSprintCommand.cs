using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;

public sealed record StartSprintCommand(Guid SprintId, DateOnly StartDate, DateOnly EndDate, string? Goal) : IRequest<Result<SprintResponse>>;
