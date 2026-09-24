using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateHolidayCalendarSettings;

public sealed class UpdateHolidayCalendarSettingsCommandHandler(
    ICurrentUser currentUser,
    IHolidayCalendarSettingsRepository settingsRepo,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateHolidayCalendarSettingsCommand, Result>
{
    public async Task<Result> Handle(UpdateHolidayCalendarSettingsCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result.Forbidden();

        var settings = await settingsRepo.GetTrackedByIdAsync(currentUser.TenantId, request.SettingsId, ct);
        if (settings is null) return Result.NotFound("Holiday calendar settings not found.");

        settings.OverrideCountryCode = string.IsNullOrWhiteSpace(request.OverrideCountryCode) ? null : request.OverrideCountryCode;
        settings.HolidaySyncEnabled = request.HolidaySyncEnabled;
        settings.UpdatedById = currentUser.UserId;
        settingsRepo.Update(settings);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
