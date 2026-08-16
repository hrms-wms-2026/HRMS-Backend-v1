using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Queries.ListSupportTickets;

public sealed record ListSupportTicketsQuery(
    string? Status,
    string? Priority,
    Guid? TenantId,
    int Page,
    int PageSize) : IRequest<Result<SupportTicketListResponseDto>>;
