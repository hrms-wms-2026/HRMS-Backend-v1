using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarConnection;

public sealed record UpdateCalendarConnectionCommand(Guid Id, string SyncDirection) : IRequest<Result<CalendarConnectionItem>>;
