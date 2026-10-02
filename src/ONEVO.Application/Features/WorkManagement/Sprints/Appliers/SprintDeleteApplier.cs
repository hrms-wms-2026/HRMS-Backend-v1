using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Appliers;

/// <summary>Applies an approved sprint.delete (still Complete/Achieved only); its tasks go back to the backlog.</summary>
public sealed class SprintDeleteApplier : SprintApplierBase
{
    public SprintDeleteApplier(ISprintRepository sprints, ISprintWriteService writes) : base(sprints, writes) { }

    public override string ActionType => WorkActionTypes.SprintDelete;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Sprint sprint, string payloadJson, CancellationToken ct)
    {
        var validation = Writes.ValidateDelete(sprint);
        if (!validation.IsSuccess)
            return validation;
        await Writes.ApplyDeleteAsync(tenantId, sprint, ct);
        return Result.Success();
    }
}
