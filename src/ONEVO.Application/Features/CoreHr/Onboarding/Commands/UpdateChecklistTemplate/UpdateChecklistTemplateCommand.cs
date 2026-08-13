using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.UpdateChecklistTemplate;

public sealed record UpdateChecklistTemplateTaskInput(
    string Title, string OwnerType, Guid? AssignedToId, Guid? AssigneePositionId, int DueOffsetDays, int? Sequence, bool IsRequired);

public record UpdateChecklistTemplateCommand(
    Guid Id,
    string Name,
    Guid? DepartmentId,
    Guid? PositionId,
    IReadOnlyList<UpdateChecklistTemplateTaskInput> Tasks) : IRequest<Result<ChecklistTemplateResponse>>;
