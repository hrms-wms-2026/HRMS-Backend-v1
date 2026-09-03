namespace ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayAttendanceStatus;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;

public sealed class GetTrayAttendanceStatusQueryHandler(
    ITrayCurrentDevice device,
    IAttendanceTodayStateService todayState,
    IAttendanceReadRepository attendance)
    : IRequestHandler<GetTrayAttendanceStatusQuery, Result<TrayAttendanceStatusDto>>
{
    public async Task<Result<TrayAttendanceStatusDto>> Handle(
        GetTrayAttendanceStatusQuery request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<TrayAttendanceStatusDto>.Failure("A valid tray device token is required.", 401);

        var contextResult = await todayState.ResolveContextAsync(device.TenantId, device.UserId, ct);
        if (!contextResult.IsSuccess)
            return Result<TrayAttendanceStatusDto>.Failure(
                contextResult.Error!, contextResult.StatusCode ?? 400);

        var context = contextResult.Value!;
        var record = await attendance.GetRecordAsync(
            context.Employee.TenantId, context.Employee.Id, context.WorkDate, ct);

        var isClockedIn = record?.ActualStart is not null && record.ActualEnd is null;
        return Result<TrayAttendanceStatusDto>.Success(new TrayAttendanceStatusDto(
            IsClockedIn: isClockedIn,
            ClockedInAtUtc: isClockedIn ? record!.ActualStart : null));
    }
}
