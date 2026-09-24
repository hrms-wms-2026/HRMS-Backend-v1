namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockOut;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record TrayClockOutCommand : IRequest<Result<AttendanceTodayResponse>>;
