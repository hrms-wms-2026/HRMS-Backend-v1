using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Queries.GetSupportTicketDetail;

public sealed record GetSupportTicketDetailQuery(Guid TicketId) : IRequest<Result<SupportTicketDetailDto>>;
