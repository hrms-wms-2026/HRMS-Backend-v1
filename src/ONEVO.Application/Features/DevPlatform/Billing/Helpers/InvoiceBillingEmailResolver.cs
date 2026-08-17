using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Billing.Helpers;

public static class InvoiceBillingEmailResolver
{
    public static async Task<string?> ResolveRecipientEmailAsync(
        Guid tenantId,
        ILegalEntityRepository legalEntities,
        IInvitationTokenRepository invitations,
        CancellationToken ct)
    {
        var legalEntity = await legalEntities.GetPrimaryByTenantIdAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(legalEntity?.Email))
            return legalEntity.Email.Trim();

        var invites = await invitations.ListByTenantAsync(tenantId, ct);
        return invites
            .Where(i => !string.IsNullOrWhiteSpace(i.InvitedEmail))
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => i.InvitedEmail.Trim())
            .FirstOrDefault();
    }
}
