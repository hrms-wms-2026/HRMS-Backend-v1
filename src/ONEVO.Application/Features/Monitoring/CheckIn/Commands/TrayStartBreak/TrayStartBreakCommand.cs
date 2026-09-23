namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayStartBreak;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record TrayStartBreakCommand : IRequest<Result<AttendanceTodayResponse>>;
