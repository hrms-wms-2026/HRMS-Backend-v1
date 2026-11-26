using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;

/// <summary>Approving the request grants the employee their position, and with it the position's roles.</summary>
public record ApproveAccessGrantRequestCommand(Guid AccessGrantRequestId)
    : IRequest<Result<ApproveAccessGrantRequestResponse>>, IAuditedCommand<Result<ApproveAccessGrantRequestResponse>>
{
    public AuditEntry DescribeAudit(Result<ApproveAccessGrantRequestResponse>? response)
        => AuditEntry.For("access_grant.approved", "access_grant_request", AccessGrantRequestId,
            details: new { employeeId = response?.Value?.EmployeeId });
}
