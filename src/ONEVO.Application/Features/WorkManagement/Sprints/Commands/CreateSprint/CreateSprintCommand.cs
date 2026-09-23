using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;

public sealed record CreateSprintCommand(Guid ProjectId, string Name, string? Goal, IReadOnlyList<Guid> TaskIds) : IRequest<Result<SprintResponse>>;
