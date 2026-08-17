using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistTemplates;

public class ListChecklistTemplatesQueryHandler(IChecklistTemplateRepository templateRepository, ICurrentUser currentUser)
    : IRequestHandler<ListChecklistTemplatesQuery, Result<ChecklistTemplateListResponse>>
{
    public async Task<Result<ChecklistTemplateListResponse>> Handle(ListChecklistTemplatesQuery request, CancellationToken ct)
    {
        var filter = new ChecklistTemplateListFilter(request.TemplateType, request.LegalEntityId, request.DepartmentId, request.PositionId, request.IncludeInactive);
        var (items, totalCount) = await templateRepository.ListAsync(currentUser.TenantId, filter, request.Page, request.PageSize, ct);

        var response = new ChecklistTemplateListResponse(
            items.Select(ChecklistTemplateResponseMapper.ToListItemResponse).ToList(), totalCount, request.Page, request.PageSize);
        return Result<ChecklistTemplateListResponse>.Success(response);
    }
}
