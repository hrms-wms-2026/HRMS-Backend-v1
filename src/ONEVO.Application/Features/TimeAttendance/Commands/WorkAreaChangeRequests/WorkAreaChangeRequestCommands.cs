using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;

public sealed record PreviewWorkAreaChangeRequestCommand(
    DateOnly Date,
    Guid RequestedWorkModeId,
    string Reason) : IRequest<Result<WorkAreaChangeRequestPreviewResponse>>;

public sealed record CreateWorkAreaChangeRequestCommand(
    DateOnly Date,
    Guid RequestedWorkModeId,
    string Reason) : IRequest<Result<WorkAreaChangeRequestResponse>>;

/// <summary><see cref="ApprovedWorkModeId"/>: the approver approves a different work mode than requested (edit before
/// approve); it must be active, in the employee's company and not already the planned one.</summary>
public sealed record ApproveWorkAreaChangeRequestCommand(Guid Id, string? ReviewComment, Guid? ApprovedWorkModeId = null)
    : IRequest<Result<WorkAreaChangeRequestResponse>>;

public sealed record RejectWorkAreaChangeRequestCommand(Guid Id, string ReviewComment)
    : IRequest<Result<WorkAreaChangeRequestResponse>>;

public sealed record CancelWorkAreaChangeRequestCommand(Guid Id)
    : IRequest<Result<WorkAreaChangeRequestResponse>>;
