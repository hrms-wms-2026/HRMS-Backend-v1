using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.Storage.File.Helpers;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetChecklistTemplate;

public class GetChecklistTemplateByIdQueryHandler(
    IChecklistTemplateRepository templateRepository, IEntityAssetRepository assets, ICurrentUser currentUser)
    : IRequestHandler<GetChecklistTemplateByIdQuery, Result<ChecklistTemplateResponse>>
{
    public async Task<Result<ChecklistTemplateResponse>> Handle(GetChecklistTemplateByIdQuery request, CancellationToken ct)
    {
        var template = await templateRepository.GetByIdAsync(currentUser.TenantId, request.Id, ct);
        if (template is null)
            return Result<ChecklistTemplateResponse>.NotFound("The checklist template could not be found.");

        var files = (await assets.ListByOwnerAsync(currentUser.TenantId, EntityAssetOwnerTypes.ChecklistTemplate, template.Id, ct))
            .Where(f => f.AssetPurpose == UploadPurposeCatalog.TaskAttachment)
            .ToList();
        return Result<ChecklistTemplateResponse>.Success(ChecklistTemplateResponseMapper.ToResponse(template, files));
    }
}
