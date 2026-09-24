using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkAllNotificationsRead;

public sealed record MarkAllNotificationsReadCommand() : IRequest<Result>;
