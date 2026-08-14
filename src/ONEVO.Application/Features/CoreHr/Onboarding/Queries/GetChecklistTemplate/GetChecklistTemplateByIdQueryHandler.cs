using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetChecklistTemplate;

public class GetChecklistTemplateByIdQueryHandler(IChecklistTemplateRepository templateRepository, ICurrentUser currentUser)
    : IRequestHandler<GetChecklistTemplateByIdQuery, Result<ChecklistTemplateResponse>>
{
    public async Task<Result<ChecklistTemplateResponse>> Handle(GetChecklistTemplateByIdQuery request, CancellationToken ct)
    {
        var template = await templateRepository.GetByIdAsync(currentUser.TenantId, request.Id, ct);
        if (template is null)
            return Result<ChecklistTemplateResponse>.NotFound("The checklist template could not be found.");

        return Result<ChecklistTemplateResponse>.Success(ChecklistTemplateResponseMapper.ToResponse(template));
    }
}
