using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;

public sealed record CompleteSprintCommand(Guid SprintId) : IRequest<Result<SprintResponse>>;
