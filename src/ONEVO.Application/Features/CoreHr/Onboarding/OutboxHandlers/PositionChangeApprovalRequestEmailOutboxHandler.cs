using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.OutboxHandlers;

public sealed class PositionChangeApprovalRequestEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public PositionChangeApprovalRequestEmailOutboxHandler(IEmailService emailService) => _emailService = emailService;

    public string Type => OutboxMessageTypes.PositionChangeApprovalRequestEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PositionChangeApprovalRequestEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("Invalid position-change approval request email payload.");

        await _emailService.SendPositionChangeApprovalRequestAsync(
            payload.ApproverEmail, payload.EmployeeName, payload.PositionName, payload.ChangeReason, ct);
    }
}
