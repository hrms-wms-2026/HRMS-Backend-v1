namespace ONEVO.Application.Features.Monitoring.PermissionConsent.Commands.RecordPermissionConsent;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.PermissionConsent.DTOs;

/// <summary>
/// Tray setup: the employee agreed to the privacy notice and tapped "Allow &amp; Continue" with
/// these permission toggles. Saved as a new consent row; the latest row is the current consent.
/// </summary>
public sealed record RecordPermissionConsentCommand(
    bool ScreenMonitoring,
    bool AppTracking,
    bool LocationAccess,
    bool CameraAccess,
    bool Notifications,
    bool KeyboardMouse) : IRequest<Result<PermissionConsentStatusDto>>;
