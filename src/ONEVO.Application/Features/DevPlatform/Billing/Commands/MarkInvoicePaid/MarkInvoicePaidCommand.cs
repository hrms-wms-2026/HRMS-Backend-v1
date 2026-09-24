using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.MarkInvoicePaid;

public sealed record MarkInvoicePaidCommand(Guid InvoiceId) : IRequest<Result<InvoiceDetailDto>>;
