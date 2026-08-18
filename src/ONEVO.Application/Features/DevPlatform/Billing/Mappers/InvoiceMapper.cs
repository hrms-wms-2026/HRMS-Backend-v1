using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.Mappers;

public static class InvoiceMapper
{
    public static InvoiceSummaryDto ToSummaryDto(SubscriptionInvoice invoice) =>
        new(
            invoice.Id,
            invoice.TenantId,
            invoice.TenantSubscriptionId,
            invoice.InvoiceNumber,
            invoice.Status,
            invoice.Currency,
            invoice.TotalAmount,
            invoice.IssuedAt,
            invoice.DueAt,
            invoice.PaidAt,
            invoice.VoidedAt,
            invoice.CreatedAt);

    public static InvoiceListResponseDto ToListResponseDto(
        IReadOnlyList<SubscriptionInvoice> invoices,
        int total,
        int page,
        int pageSize) =>
        new(
            invoices.Select(ToSummaryDto).ToList(),
            total,
            page,
            pageSize);

    public static BillingAuditLogDto ToAuditLogDto(BillingAuditLog log) =>
        new(
            log.Id,
            log.TenantId,
            log.InvoiceId,
            log.ActorAdminUserId,
            log.Action,
            log.Message,
            log.MetadataJson,
            log.CreatedAt);

    public static InvoiceDetailDto ToDetailDto(
        SubscriptionInvoice invoice,
        IReadOnlyList<BillingAuditLog> auditLogs) =>
        new(
            invoice.Id,
            invoice.TenantId,
            invoice.TenantSubscriptionId,
            invoice.InvoiceNumber,
            invoice.ExternalInvoiceId,
            invoice.GatewayProvider,
            invoice.Status,
            invoice.Currency,
            invoice.SubtotalAmount,
            invoice.TaxAmount,
            invoice.DiscountAmount,
            invoice.TotalAmount,
            invoice.PeriodStart,
            invoice.PeriodEnd,
            invoice.IssuedAt,
            invoice.DueAt,
            invoice.PaidAt,
            invoice.VoidedAt,
            invoice.CreatedAt,
            invoice.UpdatedAt,
            auditLogs.Select(ToAuditLogDto).ToList());
}
