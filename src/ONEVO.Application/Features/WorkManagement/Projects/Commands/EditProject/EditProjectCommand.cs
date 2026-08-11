using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;

public sealed record EditProjectCommand(
    Guid ProjectId,
    string Name,
    string? Description,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly TargetDate,
    string? Color,
    decimal? ActualHours,
    string? Identifier
) : IRequest<Result<ProjectDetailResponse>>;
