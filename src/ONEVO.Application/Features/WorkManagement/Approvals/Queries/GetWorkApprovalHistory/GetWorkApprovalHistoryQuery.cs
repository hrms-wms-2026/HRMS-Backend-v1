using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetWorkApprovalHistory;

public sealed record GetWorkApprovalHistoryQuery(Guid ProjectId)
    : IRequest<Result<IReadOnlyList<WorkApprovalHistoryItemResponse>>>;
