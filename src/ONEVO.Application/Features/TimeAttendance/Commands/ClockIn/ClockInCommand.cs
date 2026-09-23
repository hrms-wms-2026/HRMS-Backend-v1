using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;

public sealed record ClockInCommand(string Source) : IRequest<Result<AttendanceTodayResponse>>;
