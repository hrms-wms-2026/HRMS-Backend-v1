using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;

public sealed record EditSprintCommand(Guid SprintId, string Name, DateOnly StartDate, DateOnly EndDate) : IRequest<Result<SprintResponse>>;
