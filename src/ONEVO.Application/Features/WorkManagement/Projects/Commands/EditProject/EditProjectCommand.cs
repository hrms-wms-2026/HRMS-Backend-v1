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
    string? Identifier,
    decimal? AllocatedHours = null,
    // Icon: null keeps the current icon; empty clears it.
    string? Icon = null
) : IRequest<Result<ProjectDetailResponse>>;
