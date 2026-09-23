using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;

public sealed record StartBreakCommand : IRequest<Result<AttendanceTodayResponse>>;
