using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetChecklistTemplateFile;

/// <summary>Streams a checklist template task attachment. The file must be linked to this
/// template (owner type checklist_template); anything else is a 404.</summary>
public sealed class GetChecklistTemplateFileQueryHandler(
    IChecklistTemplateRepository templateRepository,
    IEntityAssetRepository assets,
    IFileStorageService fileStorage,
    ICurrentUser currentUser)
    : IRequestHandler<GetChecklistTemplateFileQuery, Result<FileStreamDto>>
{
    public async Task<Result<FileStreamDto>> Handle(GetChecklistTemplateFileQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var template = await templateRepository.GetByIdAsync(tenantId, request.TemplateId, ct);
        if (template is null)
            return Result<FileStreamDto>.NotFound("File not found.");

        var link = await assets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);
        if (link is null || link.OwnerType != EntityAssetOwnerTypes.ChecklistTemplate || link.OwnerId != template.Id)
            return Result<FileStreamDto>.NotFound("File not found.");

        return await fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
    }
}
