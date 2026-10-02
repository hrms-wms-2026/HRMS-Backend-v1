using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetProjectApprovalFeed;

/// <summary>Every request the caller sent or received in the project (engine requests + module invitations), pending first.</summary>
public sealed record GetProjectApprovalFeedQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<ApprovalFeedItemResponse>>>;
