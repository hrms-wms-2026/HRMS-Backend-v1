using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;

public sealed record CreateProjectCommand(
    Guid CategoryId,
    string Name,
    string Identifier,
    string? Description,
    DateOnly StartDate,
    DateOnly TargetDate,
    DateOnly ReleaseDate,
    string? Color,
    decimal? ActualHours,
    decimal DefaultObjectiveAllocatedHours,
    IReadOnlyList<CreateProjectLabelInput> Labels,
    string? LogoFileName,
    string? LogoContentType,
    Stream? LogoContent
) : IRequest<Result<ProjectCreationResponse>>;
