using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Commands.AckTrayDelivery;

/// <summary>The tray app accepted (displayed or queued) the toast. Records ClientAccepted - not "read".</summary>
public sealed record AckTrayDeliveryCommand(Guid DeliveryId) : IRequest<Result>;
