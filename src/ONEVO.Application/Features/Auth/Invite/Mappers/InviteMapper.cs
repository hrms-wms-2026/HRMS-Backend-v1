using System.Text.Json;
using ONEVO.Application.Features.Auth.Invite.DTOs.Responses;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Invite.Mappers;

internal static class InviteMapper
{
    internal static InvitationDetailDto ToDto(
        InvitationToken inv,
        Tenant tenant,
        Role? role,
        TenantAuthPolicy? policy,
        DateTimeOffset now)
    {
        var status = ComputeStatus(inv, now);
        var methods = ParseMethods(inv.CompletionMethodsJson);
        var passwordOk = methods.Count == 0
            ? true
            : methods.Contains("password", StringComparer.OrdinalIgnoreCase);
        var googleOk = methods.Count == 0
            ? true
            : methods.Contains("google", StringComparer.OrdinalIgnoreCase);
        var allowMismatch = inv.AllowGoogleEmailMismatch ?? policy?.GoogleEmailMismatchDefault ?? false;
        var domains = ParseDomains(inv.AllowedEmailDomainsJson, policy?.AllowedLoginDomainsJson);
        var (first, last) = SplitName(inv.InvitedFullName);

        return new InvitationDetailDto(
            InvitationId: inv.Id,
            TenantId: inv.TenantId,
            TenantName: tenant.Name,
            InvitedEmail: inv.InvitedEmail,
            FirstName: first,
            LastName: last,
            RoleName: role?.Name ?? string.Empty,
            ExpiresAt: inv.ExpiresAt,
            Status: status,
            PasswordSetupEnabled: passwordOk,
            GoogleSignInEnabled: googleOk,
            AllowGoogleEmailMismatch: allowMismatch,
            AllowedEmailDomains: domains);
    }

    private static string ComputeStatus(InvitationToken inv, DateTimeOffset now)
    {
        if (inv.UsedAt is not null) return "accepted";
        if (inv.RevokedAt is not null) return "revoked";
        if (inv.ExpiresAt <= now) return "expired";
        return "pending";
    }

    private static List<string> ParseMethods(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static IReadOnlyList<string> ParseDomains(string? invitationJson, string? policyJson)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var j in new[] { invitationJson, policyJson })
        {
            if (string.IsNullOrWhiteSpace(j)) continue;
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(j);
                if (list is null) continue;
                foreach (var d in list)
                    if (!string.IsNullOrWhiteSpace(d))
                        set.Add(d.Trim().ToLowerInvariant());
            }
            catch { /* ignore */ }
        }
        return set.ToList();
    }

    private static (string First, string Last) SplitName(string full)
    {
        var t = full.Trim();
        if (t.Length == 0) return ("", "");
        var sp = t.IndexOf(' ');
        if (sp < 0) return (t, "");
        return (t[..sp], t[(sp + 1)..].Trim());
    }
}
