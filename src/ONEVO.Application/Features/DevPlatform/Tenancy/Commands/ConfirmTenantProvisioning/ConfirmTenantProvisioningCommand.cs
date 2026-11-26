using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ConfirmTenantProvisioning;

/// <summary>
/// Activation confirm. On success returns 204; on incomplete sections the
/// controller maps the embedded <see cref="ProvisioningSummaryDto"/> to a 422
/// Unprocessable Entity body.
/// </summary>
public sealed record ConfirmTenantProvisioningCommand(Guid TenantId)
    : IRequest<Result<ProvisioningSummaryDto>>, IAuditedCommand<Result<ProvisioningSummaryDto>>
{
    public AuditEntry DescribeAudit(Result<ProvisioningSummaryDto>? response)
        => AuditEntry.For("tenant.provisioning_confirmed", "tenant", TenantId, TenantId, new { Status = response?.Value?.Status });
}
