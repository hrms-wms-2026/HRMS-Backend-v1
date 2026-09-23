using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;

public sealed record EndBreakCommand : IRequest<Result<AttendanceTodayResponse>>;
