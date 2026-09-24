using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Helpers;
using ONEVO.Application.Features.DevPlatform.Billing.Mappers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.ListInvoices;

public sealed class ListInvoicesQueryHandler
    : IRequestHandler<ListInvoicesQuery, Result<InvoiceListResponseDto>>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly ISubscriptionInvoiceRepository _invoiceRepo;

    public ListInvoicesQueryHandler(ISubscriptionInvoiceRepository invoiceRepo) =>
        _invoiceRepo = invoiceRepo;

    public async Task<Result<InvoiceListResponseDto>> Handle(
        ListInvoicesQuery request,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Status) && !InvoiceStatusRules.IsValid(request.Status))
            return Result<InvoiceListResponseDto>.Failure(
                $"status '{request.Status}' is not a valid invoice status.");

        int skip;
        int take;
        int page;

        if (request.Skip.HasValue && request.Take.HasValue)
        {
            skip = Math.Max(request.Skip.Value, 0);
            take = request.Take.Value <= 0 ? DefaultPageSize : Math.Min(request.Take.Value, MaxPageSize);
            page = take == 0 ? 1 : (skip / take) + 1;
        }
        else
        {
            page = request.Page <= 0 ? 1 : request.Page;
            take = request.PageSize <= 0
                ? DefaultPageSize
                : Math.Min(request.PageSize, MaxPageSize);
            skip = (page - 1) * take;
        }

        var filter = new SubscriptionInvoiceListFilter(
            request.TenantId,
            string.IsNullOrWhiteSpace(request.Status) ? null : request.Status,
            request.From,
            request.To,
            skip,
            take);

        var invoices = await _invoiceRepo.ListAsync(filter, ct);
        var total = await _invoiceRepo.CountAsync(filter, ct);

        return Result<InvoiceListResponseDto>.Success(
            InvoiceMapper.ToListResponseDto(invoices, total, page, take));
    }
}
