using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;

public sealed record RevertWorkApprovalRequestCommand(Guid RequestId) : IRequest<Result<WorkApprovalRequestResponse>>;
