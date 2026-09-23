namespace ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayAttendanceStatus;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs;

public sealed record GetTrayAttendanceStatusQuery : IRequest<Result<TrayAttendanceStatusDto>>;
