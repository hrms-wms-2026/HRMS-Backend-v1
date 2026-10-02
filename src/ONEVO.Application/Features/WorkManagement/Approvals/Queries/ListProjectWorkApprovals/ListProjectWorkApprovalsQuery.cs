using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.ListProjectWorkApprovals;

/// <summary>Scope "inbox" = pending requests the caller can decide right now; "mine" = requests the caller made.</summary>
public sealed record ListProjectWorkApprovalsQuery(Guid ProjectId, string Scope, string? Status)
    : IRequest<Result<IReadOnlyList<WorkApprovalRequestResponse>>>;
