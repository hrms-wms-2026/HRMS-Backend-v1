using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Audit.DTOs;

namespace ONEVO.Application.Features.DevPlatform.Audit.Queries.ListPlatformAuditLogs;

public sealed record ListPlatformAuditLogsQuery(
    int Page,
    int PageSize,
    Guid? ActorPlatformUserId = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    Guid? TargetTenantId = null,
    string? Outcome = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<Result<PlatformAuditLogListResponse>>;
