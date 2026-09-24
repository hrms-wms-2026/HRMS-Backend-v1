using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

public sealed record InvoiceEmailResendResponseDto(
    [property: JsonPropertyName("invoice_id")] Guid InvoiceId,
    [property: JsonPropertyName("recipient_email")] string RecipientEmail,
    [property: JsonPropertyName("delivery_status")] string DeliveryStatus);
