using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Mappers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.ListTenantInvoices;

public sealed class ListTenantInvoicesQueryHandler
    : IRequestHandler<ListTenantInvoicesQuery, Result<InvoiceListResponseDto>>
{
    private readonly ISubscriptionInvoiceRepository _invoiceRepo;
    private readonly ITenantRepository _tenantRepo;

    public ListTenantInvoicesQueryHandler(
        ISubscriptionInvoiceRepository invoiceRepo,
        ITenantRepository tenantRepo)
    {
        _invoiceRepo = invoiceRepo;
        _tenantRepo = tenantRepo;
    }

    public async Task<Result<InvoiceListResponseDto>> Handle(
        ListTenantInvoicesQuery request,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<InvoiceListResponseDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        var invoices = await _invoiceRepo.ListByTenantAsync(request.TenantId, ct);

        return Result<InvoiceListResponseDto>.Success(
            InvoiceMapper.ToListResponseDto(invoices, invoices.Count, 1, invoices.Count == 0 ? 1 : invoices.Count));
    }
}
