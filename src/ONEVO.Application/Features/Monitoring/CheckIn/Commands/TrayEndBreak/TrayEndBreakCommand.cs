namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayEndBreak;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record TrayEndBreakCommand : IRequest<Result<AttendanceTodayResponse>>;
