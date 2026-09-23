using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;

public sealed record PreviewBulkOnboardingMappingCommand(
    Guid BatchId, IReadOnlyDictionary<string, string?> Mapping) : IRequest<Result<RowPreviewResult>>;

public sealed record RowPreviewResult(
    string? FirstName, string? LastName, string? WorkEmail, string? StartDate,
    string? EmploymentType, string? WorkModeName, string? DepartmentName, string? PositionName,
    string? ChecklistTemplateName, string? EmployeeNumber);
