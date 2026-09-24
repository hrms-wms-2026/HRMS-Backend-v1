using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.ArchiveChecklistTemplate;

/// <summary>Soft-deactivate only. There is no delete path anywhere in this feature:
/// employee_checklist_tasks.template_id is ON DELETE RESTRICT, so historical instantiated
/// tasks always keep a valid template reference even after it is archived.</summary>
public class ArchiveChecklistTemplateCommandHandler(IChecklistTemplateRepository templateRepository, ICurrentUser currentUser)
    : IRequestHandler<ArchiveChecklistTemplateCommand, Result>
{
    public async Task<Result> Handle(ArchiveChecklistTemplateCommand request, CancellationToken ct)
    {
        var template = await templateRepository.GetTrackedByIdAsync(currentUser.TenantId, request.Id, ct);
        if (template is null)
            return Result.NotFound("The checklist template could not be found.");

        template.IsActive = false;
        await templateRepository.SaveChangesAsync(ct);

        return Result.Success();
    }
}
