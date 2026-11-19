using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;

/// <summary>API spelling of channels: <c>in_app</c>, <c>email</c>, <c>tray</c>.</summary>
public static class NotificationChannelNames
{
    public const string InApp = "in_app";
    public const string Email = "email";
    public const string Tray = "tray";

    private static readonly (NotificationChannels Channel, string Name)[] Map =
        [(NotificationChannels.InApp, InApp), (NotificationChannels.Email, Email), (NotificationChannels.Tray, Tray)];

    public static IReadOnlyList<string> ToNames(NotificationChannels channels)
        => Map.Where(m => channels.HasFlag(m.Channel)).Select(m => m.Name).ToList();

    public static string ToName(NotificationChannels channel) => Map.Single(m => m.Channel == channel).Name;

    /// <summary>Parses channel names; returns false on any unknown name.</summary>
    public static bool TryParse(IEnumerable<string>? names, out NotificationChannels channels, out string? unknown)
    {
        channels = NotificationChannels.None;
        unknown = null;
        foreach (var name in names ?? [])
        {
            var match = Map.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
            if (match.Name is null)
            {
                unknown = name;
                return false;
            }
            channels |= match.Channel;
        }
        return true;
    }

    /// <summary>Individual channels in a mask, in delivery order.</summary>
    public static IEnumerable<NotificationChannels> Each(NotificationChannels channels)
        => Map.Where(m => channels.HasFlag(m.Channel)).Select(m => m.Channel);
}
