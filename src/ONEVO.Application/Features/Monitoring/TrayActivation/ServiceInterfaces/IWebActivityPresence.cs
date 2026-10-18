namespace ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

/// <summary>
/// When each user last made an authenticated request from the web app. An open tab keeps this
/// fresh on its own (the notification bell polls every minute). In-memory, so it assumes a single
/// API instance, the same as <see cref="ITrayConnectionPresence"/>.
/// </summary>
public interface IWebActivityPresence
{
    DateTimeOffset? LastSeenAt(Guid tenantId, Guid userId);
}
