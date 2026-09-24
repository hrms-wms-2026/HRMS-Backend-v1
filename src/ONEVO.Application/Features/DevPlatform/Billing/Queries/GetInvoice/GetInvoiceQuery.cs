using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.GetInvoice;

public sealed record GetInvoiceQuery(Guid InvoiceId) : IRequest<Result<InvoiceDetailDto>>;
