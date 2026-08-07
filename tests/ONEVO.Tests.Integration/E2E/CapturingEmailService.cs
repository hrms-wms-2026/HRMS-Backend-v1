using System.Collections.Concurrent;
using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Tests.Integration.E2E;

/// <summary>
/// Test double for IEmailService that records every send instead of talking to a
/// provider. The E2E flow uses it to capture the plaintext invite token, which the
/// real system only ever emits inside the tenant_owner_invite email.
/// </summary>
public sealed class CapturingEmailService : IEmailService
{
    public sealed record SentTemplate(string To, string TemplateId, JsonElement Data);

    private readonly ConcurrentQueue<SentTemplate> _templates = new();

    public IReadOnlyCollection<SentTemplate> Templates => _templates;

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SendTemplateAsync(string to, string templateId, object templateData, CancellationToken ct = default)
    {
        var data = JsonSerializer.SerializeToElement(templateData);
        _templates.Enqueue(new SentTemplate(to, templateId, data));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string to, string resetToken, string? tenantSlug = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SendAdminPasswordResetAsync(string to, string resetToken, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SendAdminPasswordChangedAsync(string to, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SendPlatformManagerInviteAsync(string to, string fullName, string inviteToken, CancellationToken ct = default)
        => Task.CompletedTask;

    public string? LastInviteToken()
    {
        SentTemplate? last = null;
        foreach (var t in _templates)
            if (t.TemplateId == "tenant_owner_invite")
                last = t;

        if (last is null)
            return null;

        return last.Data.TryGetProperty("invite_token", out var token)
            ? token.GetString()
            : null;
    }
}
