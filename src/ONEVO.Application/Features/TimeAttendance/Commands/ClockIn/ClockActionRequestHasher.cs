using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;

public static class ClockActionRequestHasher
{
    public static string Hash(ClockInCommand command)
    {
        var normalized = JsonSerializer.Serialize(new
        {
            command.AgentId,
            capture = command.Capture is null
                ? null
                : new
                {
                    command.Capture.Latitude,
                    command.Capture.Longitude,
                    command.Capture.AccuracyMeters,
                    command.Capture.CapturedAt,
                    PermissionState =
                        command.Capture.PermissionState.Trim().ToLowerInvariant()
                },
            LocalNetworkClass =
                command.LocalNetworkClass?.Trim().ToLowerInvariant(),
            command.WifiBssidHash,
            command.GatewayMacHash,
            command.VpnDetected,
            command.VerificationRecordId
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}

