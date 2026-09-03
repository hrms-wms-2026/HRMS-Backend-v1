namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockOut;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;

public sealed class TrayClockOutCommandHandler(
    ITrayCurrentDevice device,
    IAttendanceTodayStateService todayState,
    ClockOutCommandHandler inner)
    : IRequestHandler<TrayClockOutCommand, Result<AttendanceTodayResponse>>
{
    public async Task<Result<AttendanceTodayResponse>> Handle(
        TrayClockOutCommand request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<AttendanceTodayResponse>.Failure("A valid tray device token is required.", 401);

        var contextResult = await todayState.ResolveContextAsync(device.TenantId, device.UserId, ct);
        if (!contextResult.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(
                contextResult.Error!, contextResult.StatusCode ?? 400);

        return await inner.HandleForContextAsync(contextResult.Value!, ct);
    }
}
