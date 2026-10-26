namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

/// <summary>Delivery channels a notification event can be routed to. Stored as an int bit mask.</summary>
[Flags]
public enum NotificationChannels
{
    None = 0,
    InApp = 1,
    Email = 2,
    Tray = 4
}
