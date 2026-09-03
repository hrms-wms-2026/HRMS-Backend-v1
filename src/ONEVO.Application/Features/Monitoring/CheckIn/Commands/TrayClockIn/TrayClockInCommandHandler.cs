namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockIn;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;

public sealed class TrayClockInCommandHandler(
    ITrayCurrentDevice device,
    IAttendanceTodayStateService todayState,
    ClockInCommandHandler inner)
    : IRequestHandler<TrayClockInCommand, Result<AttendanceTodayResponse>>
{
    public async Task<Result<AttendanceTodayResponse>> Handle(
        TrayClockInCommand request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<AttendanceTodayResponse>.Failure("A valid tray device token is required.", 401);

        var contextResult = await todayState.ResolveContextAsync(device.TenantId, device.UserId, ct);
        if (!contextResult.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(
                contextResult.Error!, contextResult.StatusCode ?? 400);

        return await inner.HandleForContextAsync(contextResult.Value!, "tray", ct);
    }
}
