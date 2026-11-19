using ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Helpers;

/// <summary>
/// Turns a day of device-state samples (the tray reports idle/active every few minutes) into the
/// short timeline a reviewer reads: runs of active / idle, and "offline" wherever the device went
/// quiet for longer than <see cref="OfflineGap"/> (shut down, asleep, tray not running).
/// Pure logic - unit tested directly.
/// </summary>
public static class DeviceActivityTimeline
{
    public const string Active = "active";
    public const string Idle = "idle";
    public const string Offline = "offline";

    /// <summary>A silence longer than this between two samples is treated as the device being off.</summary>
    public static readonly TimeSpan OfflineGap = TimeSpan.FromMinutes(10);

    public const int MaxSegments = 300;

    public static DeviceActivityEvidenceDto Build(IReadOnlyList<DeviceStateSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return new DeviceActivityEvidenceDto(null, null, 0, 0, 0, [], [], Truncated: false);

        var ordered = snapshots.OrderBy(s => s.CapturedAt).ToList();
        var segments = new List<DeviceActivitySegmentDto>();
        double active = 0, idle = 0, offline = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var state = current.IsIdle ? Idle : Active;
            if (i == ordered.Count - 1)
            {
                // The last sample has no successor: it marks a point in time, not a stretch.
                Append(segments, state, current.CapturedAt, current.CapturedAt);
                break;
            }

            var next = ordered[i + 1].CapturedAt;
            var gap = next - current.CapturedAt;
            if (gap > OfflineGap)
            {
                Append(segments, state, current.CapturedAt, current.CapturedAt);
                Append(segments, Offline, current.CapturedAt, next);
                offline += gap.TotalMinutes;
            }
            else
            {
                Append(segments, state, current.CapturedAt, next);
                if (current.IsIdle) idle += gap.TotalMinutes; else active += gap.TotalMinutes;
            }
        }

        var truncated = segments.Count > MaxSegments;
        return new DeviceActivityEvidenceDto(
            ordered[0].CapturedAt,
            ordered[^1].CapturedAt,
            (int)Math.Round(active),
            (int)Math.Round(idle),
            (int)Math.Round(offline),
            ordered.Select(s => s.AgentDeviceId).Distinct().ToList(),
            truncated ? segments.Take(MaxSegments).ToList() : segments,
            truncated);
    }

    /// <summary>Extends the previous segment when the state is unchanged and the times touch,
    /// so a day of per-minute samples collapses to a handful of runs.</summary>
    private static void Append(List<DeviceActivitySegmentDto> segments, string state, DateTimeOffset start, DateTimeOffset end)
    {
        if (segments.Count > 0)
        {
            var last = segments[^1];
            if (last.State == state && last.End >= start)
            {
                segments[^1] = last with { End = end > last.End ? end : last.End };
                return;
            }
        }
        segments.Add(new DeviceActivitySegmentDto(start, end, state));
    }
}
