using System.Text.Json;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Outbox;

namespace ONEVO.Application.Features.Leave.Approval.OutboxHandlers;

public sealed class LeaveApprovalEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _email;
    private readonly IEmployeeRepository _employees;

    public LeaveApprovalEmailOutboxHandler(IEmailService email, IEmployeeRepository employees, string type)
    {
        _email = email;
        _employees = employees;
        Type = type;
    }

    public string Type { get; }

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        switch (Type)
        {
            case OutboxMessageTypes.LeaveRequestApproved:
                await SendApprovedAsync(JsonSerializer.Deserialize<LeaveRequestApprovedPayload>(payloadJson), ct);
                return;
            case OutboxMessageTypes.LeaveRequestRejected:
                await SendRejectedAsync(JsonSerializer.Deserialize<LeaveRequestRejectedPayload>(payloadJson), ct);
                return;
            case OutboxMessageTypes.LeaveInformationRequested:
                await SendInfoAsync(JsonSerializer.Deserialize<LeaveInformationRequestedPayload>(payloadJson), ct);
                return;
            case OutboxMessageTypes.LeaveRequestCancelled:
                await SendCancelledAsync(JsonSerializer.Deserialize<LeaveRequestCancelledPayload>(payloadJson), ct);
                return;
            default:
                return;
        }
    }

    private async Task SendApprovedAsync(LeaveRequestApprovedPayload? payload, CancellationToken ct)
    {
        if (payload is null) return;
        var employee = await _employees.GetByIdAsync(payload.TenantId, payload.EmployeeId, ct);
        if (employee is null || string.IsNullOrWhiteSpace(employee.Email)) return;
        await _email.SendAsync(
            employee.Email,
            "Your leave request was approved",
            $"<p>Your leave from {payload.StartAt:yyyy-MM-dd} to {payload.EndAt:yyyy-MM-dd} was approved.</p>",
            ct);
    }

    private async Task SendRejectedAsync(LeaveRequestRejectedPayload? payload, CancellationToken ct)
    {
        if (payload is null) return;
        var employee = await _employees.GetByIdAsync(payload.TenantId, payload.EmployeeId, ct);
        if (employee is null || string.IsNullOrWhiteSpace(employee.Email)) return;
        await _email.SendAsync(
            employee.Email,
            "Your leave request was rejected",
            $"<p>Your leave from {payload.StartAt:yyyy-MM-dd} to {payload.EndAt:yyyy-MM-dd} was rejected.</p><p>{System.Net.WebUtility.HtmlEncode(payload.Reason)}</p>",
            ct);
    }

    private async Task SendInfoAsync(LeaveInformationRequestedPayload? payload, CancellationToken ct)
    {
        if (payload is null) return;
        var employee = await _employees.GetByIdAsync(payload.TenantId, payload.EmployeeId, ct);
        if (employee is null || string.IsNullOrWhiteSpace(employee.Email)) return;
        await _email.SendAsync(
            employee.Email,
            "More information needed for your leave request",
            $"<p>{System.Net.WebUtility.HtmlEncode(payload.Question)}</p>",
            ct);
    }

    private async Task SendCancelledAsync(LeaveRequestCancelledPayload? payload, CancellationToken ct)
    {
        if (payload is null) return;
        var employee = await _employees.GetByIdAsync(payload.TenantId, payload.EmployeeId, ct);
        if (employee is null || string.IsNullOrWhiteSpace(employee.Email)) return;
        await _email.SendAsync(
            employee.Email,
            "A leave request was cancelled",
            $"<p>Leave from {payload.OriginalStartAt:yyyy-MM-dd} to {payload.OriginalEndAt:yyyy-MM-dd} was cancelled.</p>",
            ct);
    }
}
