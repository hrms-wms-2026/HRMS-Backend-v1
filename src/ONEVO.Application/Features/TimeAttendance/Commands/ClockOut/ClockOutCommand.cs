using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;

public sealed record ClockOutCommand : IRequest<Result<AttendanceTodayResponse>>;
