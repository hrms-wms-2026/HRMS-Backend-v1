using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintStatus;

public sealed record SetSprintStatusCommand(Guid SprintId, string Status) : IRequest<Result<SprintResponse>>;
