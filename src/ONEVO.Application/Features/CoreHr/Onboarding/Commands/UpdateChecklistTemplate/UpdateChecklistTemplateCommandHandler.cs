using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.UpdateChecklistTemplate;

public class UpdateChecklistTemplateCommandHandler(
    IChecklistTemplateRepository templateRepository,
    IDepartmentRepository departmentRepository,
    IPositionRepository positionRepository,
    ChecklistTemplateTaskInputResolver taskInputResolver,
    ITaskAssetLinker assetLinker,
    IEntityAssetRepository assets,
    ICurrentUser currentUser)
    : IRequestHandler<UpdateChecklistTemplateCommand, Result<ChecklistTemplateResponse>>
{
    public async Task<Result<ChecklistTemplateResponse>> Handle(UpdateChecklistTemplateCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var template = await templateRepository.GetTrackedByIdAsync(tenantId, request.Id, ct);
        if (template is null)
            return Result<ChecklistTemplateResponse>.NotFound("The checklist template could not be found.");

        if (template.LegalEntityId is null)
            return Result<ChecklistTemplateResponse>.UnprocessableEntity("This template has no company assigned and cannot be updated through this endpoint.");

        var scopeError = await ChecklistTemplateScopeValidator.ValidateAsync(
            tenantId, template.LegalEntityId.Value, request.DepartmentId, request.PositionId, departmentRepository, positionRepository, ct);
        if (scopeError is not null)
            return Result<ChecklistTemplateResponse>.UnprocessableEntity(scopeError);

        var inputs = request.Tasks
            .Select(t => new ChecklistTemplateTaskInput(t.Title, t.OwnerType, t.AssignedToId, t.AssigneePositionId, t.DueOffsetDays, t.Sequence, t.IsRequired,
                t.IsBypassable, t.BypassPenaltyDescription, t.Category, t.Description, t.Priority, t.AttachmentFileIds))
            .ToList();
        var resolved = await taskInputResolver.ResolveAsync(tenantId, template.LegalEntityId.Value, inputs, ct);
        if (!resolved.IsSuccess)
            return Result<ChecklistTemplateResponse>.Failure(resolved.Error!, resolved.StatusCode ?? 422);

        // Direct mutation of the tracked entity - no Update()/blind-overwrite call exists on
        // IChecklistTemplateRepository. EF's change tracker persists exactly these field
        // changes on SaveChangesAsync.
        template.Name = request.Name.Trim();
        template.DepartmentId = request.DepartmentId;
        template.PositionId = request.PositionId;
        var (tasks, files) = await ChecklistTemplateAttachmentSync.SyncAsync(
            tenantId, currentUser.UserId, template.Id, resolved.Value!, assetLinker, assets, ct);
        template.TasksJson = ChecklistTaskJsonContract.SerializeTemplateTasks(tasks);

        await templateRepository.SaveChangesAsync(ct);

        return Result<ChecklistTemplateResponse>.Success(ChecklistTemplateResponseMapper.ToResponse(template, tasks, files));
    }
}
