using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Queries.CheckModuleCapacity;

/// <summary>Live capacity check for the Module create/edit form. ModuleId is set when editing; the
/// people counted are the given members, the Module's current members and owner (edit) and the caller
/// (create, who becomes the owner).</summary>
public sealed record CheckModuleCapacityQuery(
    Guid ProjectId, Guid? ModuleId, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours,
    IReadOnlyList<Guid>? MemberEmployeeIds) : IRequest<Result<ModuleCapacityCheckResponse>>;
