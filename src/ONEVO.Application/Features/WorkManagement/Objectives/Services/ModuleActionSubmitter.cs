using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public sealed class ModuleActionSubmitter : IModuleActionSubmitter
{
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkApprovalEngine _approvals;
    private readonly IWorkNotificationEngine _notifications;

    public ModuleActionSubmitter(
        IObjectiveRepository objectives, IUnitOfWork unitOfWork, IWorkApprovalEngine approvals, IWorkNotificationEngine notifications)
    {
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _approvals = approvals;
        _notifications = notifications;
    }

    public async Task<Result<ModuleActionOutcome>> SubmitAsync(
        Guid tenantId, Guid actorEmployeeId, Objective module, string actionType, object? input,
        Func<Objective, CancellationToken, Task<Result>> apply, IReadOnlyCollection<Guid>? extraRecipients = null,
        CancellationToken ct = default)
    {
        var parentOwner = module.ParentObjectiveId is { } parentId
            ? (await _objectives.GetByIdForTenantAsync(tenantId, parentId, ct))?.OwnerId
            : null;
        var recipients = new List<Guid> { module.OwnerId };
        if (parentOwner is { } p)
            recipients.Add(p);
        if (extraRecipients is not null)
            recipients.AddRange(extraRecipients);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // Modules created before creator positions were stamped have none; their position is the
            // parent (spec §5.2) - the engine's own null fallback would use the Module itself.
            var decision = await _approvals.SubmitAsync(new WorkAction(
                tenantId, module.ProjectId, actorEmployeeId,
                actionType, WorkTargetTypes.Module,
                TargetId: module.Id, TargetTitle: module.Title,
                TargetModuleId: module.Id, PositionModuleId: module.CreatorPositionObjectiveId ?? module.ParentObjectiveId,
                PayloadJson: input is null ? "{}" : JsonSerializer.Serialize(input, input.GetType(), ModulePayloadJson.Options),
                TargetUpdatedAt: module.UpdatedAt ?? module.CreatedAt), innerCt);
            if (!decision.IsSuccess)
                return Result<ModuleActionOutcome>.Failure(decision.Error!, decision.StatusCode ?? 400);

            if (!decision.Value!.IsDirect)
            {
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<ModuleActionOutcome>.Success(new ModuleActionOutcome(false, decision.Value.ApprovalRequestId, null));
            }

            var tracked = await _objectives.GetTrackedByIdForTenantAsync(tenantId, module.Id, innerCt);
            if (tracked is null)
                return Result<ModuleActionOutcome>.NotFound("Objective not found.");

            var applied = await apply(tracked, innerCt);
            if (!applied.IsSuccess)
                return Result<ModuleActionOutcome>.Failure(applied.Error!, applied.StatusCode ?? 400);

            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, module.ProjectId, actorEmployeeId, WorkNotificationKinds.Direct,
                actionType, WorkTargetTypes.Module, module.Id, module.Title, null, recipients), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<ModuleActionOutcome>.Success(new ModuleActionOutcome(true, null, tracked));
        }, ct);
    }
}
