using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetUnreadCount;

public sealed record GetUnreadCountQuery() : IRequest<Result<int>>;
