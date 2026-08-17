using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistTemplates;

public record ListChecklistTemplatesQuery(
    string? TemplateType, Guid LegalEntityId, Guid? DepartmentId, Guid? PositionId, bool IncludeInactive, int Page, int PageSize)
    : IRequest<Result<ChecklistTemplateListResponse>>;
