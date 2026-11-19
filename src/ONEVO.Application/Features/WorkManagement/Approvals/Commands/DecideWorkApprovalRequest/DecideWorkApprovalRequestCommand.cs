using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;

public enum WorkApprovalDecision { Approve, Reject, Cancel }

/// <summary>EditedPayloadJson is only honoured for Approve (the approver may adjust the change before applying it).</summary>
public sealed record DecideWorkApprovalRequestCommand(
    Guid RequestId, WorkApprovalDecision Decision, string? EditedPayloadJson, string? Comment)
    : IRequest<Result<WorkApprovalRequestResponse>>;
