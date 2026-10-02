using System.Text.Json;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs;

/// <summary>payload_json of module.edit. Same fields as the old EditObjectiveRequestPayload.</summary>
public sealed record ModuleEditInput(string Title, string? Description, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours);

/// <summary>payload_json of module.transfer.</summary>
public sealed record ModuleTransferInput(Guid NewHeadEmployeeId);

/// <summary>payload_json of module.allocation_extend. The approver may lower RequestedAdditionalHours.</summary>
public sealed record ModuleAllocationExtendInput(decimal RequestedAdditionalHours, string Reason);

/// <summary>payload_json of module.member_add. RequestedByEmployeeId travels in the payload (not just
/// the WorkApprovalRequest column) so the Applier - which only sees payloadJson - knows who to credit
/// as "invited you" on the invitation it creates once approved.</summary>
public sealed record ModuleMemberAddInput(Guid EmployeeId, Guid RequestedByEmployeeId);

/// <summary>payload_json of module.member_remove.</summary>
public sealed record ModuleMemberRemoveInput(Guid EmployeeId);

public static class ModulePayloadJson
{
    /// <summary>Writes camelCase; reads case-insensitively, so PascalCase rows copied from the old table still parse.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
