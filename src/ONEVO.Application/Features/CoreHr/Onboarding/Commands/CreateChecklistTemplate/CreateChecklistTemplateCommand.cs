using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.CreateChecklistTemplate;

public sealed record CreateChecklistTemplateTaskInput(
    string Title, string OwnerType, Guid? AssignedToId, Guid? AssigneePositionId, int DueOffsetDays, int? Sequence, bool IsRequired);

public record CreateChecklistTemplateCommand(
    string Name,
    string TemplateType,
    Guid LegalEntityId,
    Guid? DepartmentId,
    Guid? PositionId,
    IReadOnlyList<CreateChecklistTemplateTaskInput> Tasks) : IRequest<Result<ChecklistTemplateResponse>>;
