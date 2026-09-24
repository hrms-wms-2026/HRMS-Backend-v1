using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.ListInvoices;

public sealed record ListInvoicesQuery(
    Guid? TenantId,
    string? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize,
    int? Skip,
    int? Take) : IRequest<Result<InvoiceListResponseDto>>;
