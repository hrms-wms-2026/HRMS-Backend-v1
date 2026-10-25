using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

/// <summary>Creates a single-leave-type policy, its version 1 (in force from today) and its
/// company/department/position assignments.</summary>
public sealed record CreateLeavePolicyCommand(
    Guid LeaveTypeId,
    string Name,
    string? Description,
    LeavePolicyRulesInput Rules,
    IReadOnlyList<LeavePolicyAssignmentInput> Assignments,
    bool ReplaceConflicts) : IRequest<Result<LeavePolicyWriteResult>>;
