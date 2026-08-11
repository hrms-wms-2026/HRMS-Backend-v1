using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;

/// <summary>Employee-only email contract. It deliberately carries no role or permission data.</summary>
public sealed record EmployeeOnboardingInviteEmailPayload(
    Guid TenantId, Guid LegalEntityId, Guid EmployeeId, Guid InvitationTokenId,
    string Email, string FirstName, string LastName, string RawToken, DateTimeOffset ExpiresAt);

public sealed class EmployeeOnboardingInviteEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;
    public EmployeeOnboardingInviteEmailOutboxHandler(IEmailService emailService) => _emailService = emailService;
    public string Type => OutboxMessageTypes.EmployeeOnboardingInviteEmail;

    public Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<EmployeeOnboardingInviteEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("employee_onboarding_invite_email payload is empty.");
        return _emailService.SendEmployeeOnboardingInviteAsync(payload.Email, payload.FirstName, payload.LastName, payload.RawToken, ct);
    }
}
