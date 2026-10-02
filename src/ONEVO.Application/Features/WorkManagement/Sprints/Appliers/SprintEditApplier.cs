using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Appliers;

/// <summary>Applies an approved sprint.edit. Stale if the sprint changed after the request.</summary>
public sealed class SprintEditApplier : SprintApplierBase
{
    public SprintEditApplier(ISprintRepository sprints, ISprintWriteService writes) : base(sprints, writes) { }

    public override string ActionType => WorkActionTypes.SprintEdit;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Sprint sprint, string payloadJson, CancellationToken ct)
    {
        var input = Read<SprintEditInput>(payloadJson);
        if (input is null || string.IsNullOrWhiteSpace(input.Name))
            return Result.Failure("The sprint edit request has no name.");
        return await Writes.ApplyEditAsync(tenantId, actorEmployeeId, sprint, input, ct);
    }
}
