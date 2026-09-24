using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.OutboxHandlers;

public sealed record AdminPasswordChangedEmailPayload(string Email);

/// <summary>Sends the post-reset security notification email. Safe to retry.</summary>
public sealed class AdminPasswordChangedEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public AdminPasswordChangedEmailOutboxHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public string Type => OutboxMessageTypes.AdminPasswordChangedEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AdminPasswordChangedEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("admin_password_changed_email payload is empty.");

        await _emailService.SendAdminPasswordChangedAsync(payload.Email, ct);
    }
}
