using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.OutboxHandlers;

/// <summary>Payload for the admin password reset email outbox message.</summary>
public sealed record AdminPasswordResetEmailPayload(string Email, string RawToken);

/// <summary>
/// Sends the admin password reset email from the outbox. Safe to retry: resending the same
/// reset link is harmless and the token stays valid until it expires or is consumed.
/// </summary>
public sealed class AdminPasswordResetEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public AdminPasswordResetEmailOutboxHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public string Type => OutboxMessageTypes.AdminPasswordResetEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AdminPasswordResetEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("admin_password_reset_email payload is empty.");

        await _emailService.SendAdminPasswordResetAsync(payload.Email, payload.RawToken, ct);
    }
}
