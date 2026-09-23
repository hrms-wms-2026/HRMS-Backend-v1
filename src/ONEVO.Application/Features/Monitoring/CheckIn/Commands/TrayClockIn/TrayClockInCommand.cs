namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockIn;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record TrayClockInCommand : IRequest<Result<AttendanceTodayResponse>>;
