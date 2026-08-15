using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Billing.OutboxHandlers;

public sealed class InvoiceEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public InvoiceEmailOutboxHandler(IEmailService emailService) => _emailService = emailService;

    public string Type => OutboxMessageTypes.InvoiceEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<InvoiceEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("invoice_email payload is empty.");

        await _emailService.SendInvoiceEmailAsync(
            payload.RecipientEmail,
            new
            {
                tenant_name = payload.TenantName,
                invoice_number = payload.InvoiceNumber,
                status = payload.Status,
                currency = payload.Currency,
                subtotal_amount = payload.SubtotalAmount,
                tax_amount = payload.TaxAmount,
                discount_amount = payload.DiscountAmount,
                total_amount = payload.TotalAmount,
                period_start = payload.PeriodStart,
                period_end = payload.PeriodEnd,
                due_at = payload.DueAt,
                paid_at = payload.PaidAt,
                is_receipt = payload.IsReceipt
            },
            ct);
    }
}
