using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;

public static class GitHubTenantApprovalMapper
{
    public static GitHubTenantApprovalDto ToDto(TenantIntegrationCredential? approval)
    {
        if (approval is null)
        {
            return new GitHubTenantApprovalDto(false, "disconnected", null, null, null);
        }

        return new GitHubTenantApprovalDto(
            approval.Status == "connected",
            approval.Status,
            approval.ConnectedAt,
            approval.ConnectedByUserId,
            approval.DisconnectedAt);
    }
}
