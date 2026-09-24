using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;

/// <param name="ApprovedEdit">For an Edit-type request: the fields to apply, overriding what was
/// originally requested - lets the approver adjust the request before approving it. Falls back to
/// the originally requested payload when null.</param>
public sealed record ApproveObjectiveChangeRequestCommand(
    Guid RequestId,
    decimal? ApprovedAdditionalHours = null,
    EditObjectiveRequestPayload? ApprovedEdit = null
) : IRequest<Result>;
