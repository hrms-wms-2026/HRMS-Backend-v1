using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Queries.GetPendingTrayDeliveries;

/// <summary>Generic notification deliveries waiting for the calling tray device's user. Only clients that
/// support generic notifications call this; legacy clients keep using the monitoring pending endpoint.</summary>
public sealed record GetPendingTrayDeliveriesQuery : IRequest<Result<IReadOnlyList<TrayDeliveryDto>>>;
