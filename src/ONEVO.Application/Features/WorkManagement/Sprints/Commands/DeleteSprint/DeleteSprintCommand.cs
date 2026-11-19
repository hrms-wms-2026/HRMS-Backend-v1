using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.DeleteSprint;

/// <summary>Deletes a Complete or Achieved sprint; its tasks go back to the backlog.</summary>
public sealed record DeleteSprintCommand(Guid SprintId) : IRequest<Result<SprintWriteOutcome>>;
