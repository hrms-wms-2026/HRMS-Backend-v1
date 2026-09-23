using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;

/// <summary>Disposition is "backlog" (clears SprintId on every incomplete task) or "sprint"
/// (moves them to TargetSprintId, required in that case).</summary>
public sealed record CompleteSprintCommand(Guid SprintId, string Disposition, Guid? TargetSprintId) : IRequest<Result<SprintResponse>>;
