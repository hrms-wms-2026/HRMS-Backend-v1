namespace ONEVO.Application.Features.Monitoring.PermissionConsent.Queries.GetPermissionConsentStatus;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.PermissionConsent.DTOs;

/// <summary>
/// Whether the tray device's employee already agreed to the privacy notice and allowed the
/// tray's permissions. Tray device setup skips both screens when they did.
/// </summary>
public sealed record GetPermissionConsentStatusQuery : IRequest<Result<PermissionConsentStatusDto>>;
