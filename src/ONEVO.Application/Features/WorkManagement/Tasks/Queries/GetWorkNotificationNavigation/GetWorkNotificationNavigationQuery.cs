using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetWorkNotificationNavigation;

public sealed record GetWorkNotificationNavigationQuery(string RelatedEntityType, Guid RelatedEntityId)
    : IRequest<Result<WorkNotificationNavigationResponse>>;
