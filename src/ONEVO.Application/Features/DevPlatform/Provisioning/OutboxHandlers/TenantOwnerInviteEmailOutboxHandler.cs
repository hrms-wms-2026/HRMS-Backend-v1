using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Provisioning.OutboxHandlers;

/// <summary>Payload for the tenant owner invitation email outbox message.</summary>
public sealed record TenantOwnerInviteEmailPayload(
    Guid TenantId,
    string TenantName,
    string Email,
    string FirstName,
    string InviteToken,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Sends the tenant owner invitation email from the outbox. Safe to retry: sending
/// the same invite email twice is harmless and the token stays valid until expiry.
/// </summary>
public sealed class TenantOwnerInviteEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;
    private readonly ITenantRepository _tenants;

    public TenantOwnerInviteEmailOutboxHandler(IEmailService emailService, ITenantRepository tenants)
    {
        _emailService = emailService;
        _tenants = tenants;
    }

    public string Type => OutboxMessageTypes.TenantOwnerInviteEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<TenantOwnerInviteEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("tenant_owner_invite_email payload is empty.");

        // The enqueueing transaction may not have known the tenant name yet
        // (tenant row unsaved at enqueue time); it is durable by the time we run.
        // The slug is always read here: the invitation is tenant-scoped (RLS), so the link must name the
        // tenant - as a subdomain in Subdomain mode, as ?tenant= in SingleHost mode - or the accept page
        // cannot find it.
        var tenant = await _tenants.GetByIdAsync(payload.TenantId, ct);
        var tenantName = string.IsNullOrEmpty(payload.TenantName)
            ? tenant?.Name ?? string.Empty
            : payload.TenantName;
        var tenantSlug = tenant?.Slug ?? string.Empty;

        await _emailService.SendTemplateAsync(
            payload.Email,
            templateId: "tenant_owner_invite",
            templateData: new
            {
                tenant_id = payload.TenantId,
                tenant_company = tenantName,
                tenant_slug = tenantSlug,
                invited_first_name = payload.FirstName,
                invited_email = payload.Email,
                invite_token = payload.InviteToken,
                invite_expires_at = payload.ExpiresAt
            },
            ct);
    }
}
