using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.OutboxHandlers;

public sealed class PlatformManagerInviteEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public PlatformManagerInviteEmailOutboxHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public string Type => OutboxMessageTypes.PlatformManagerInviteEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PlatformManagerInviteEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("platform_manager_invite_email payload is empty.");

        await _emailService.SendPlatformManagerInviteAsync(payload.Email, payload.FullName, payload.RawToken, ct);
    }
}
