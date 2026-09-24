using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.VoidInvoice;

public sealed record VoidInvoiceCommand(Guid InvoiceId) : IRequest<Result<InvoiceDetailDto>>;
