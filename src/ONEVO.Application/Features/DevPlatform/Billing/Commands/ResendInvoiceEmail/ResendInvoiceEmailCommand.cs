using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.ResendInvoiceEmail;

public sealed record ResendInvoiceEmailCommand(Guid InvoiceId)
    : IRequest<Result<InvoiceEmailResendResponseDto>>;
