using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.ListTenantInvoices;

public sealed record ListTenantInvoicesQuery(Guid TenantId) : IRequest<Result<InvoiceListResponseDto>>;
