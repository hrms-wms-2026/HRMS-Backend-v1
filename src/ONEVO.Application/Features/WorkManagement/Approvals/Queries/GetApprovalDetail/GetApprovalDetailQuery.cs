using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetApprovalDetail;

/// <summary>The explanation card of one Approvals-page row. Source is "engine" or "invitation".</summary>
public sealed record GetApprovalDetailQuery(Guid Id, string Source) : IRequest<Result<ApprovalDetailResponse>>;
