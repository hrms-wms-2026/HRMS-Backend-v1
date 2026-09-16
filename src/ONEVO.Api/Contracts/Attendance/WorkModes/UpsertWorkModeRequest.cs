namespace ONEVO.Api.Contracts.Attendance.WorkModes;

public record UpsertWorkModeRequest(
    string Name,
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired,
    bool SelfRegistersLocation,
    bool AllowsDailyLocationChoice);
