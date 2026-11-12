using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Audit.DTOs;
using ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;

namespace ONEVO.Application.Features.DevPlatform.Audit.Queries.ListPlatformAuditLogs;

public sealed class ListPlatformAuditLogsQueryHandler
    : IRequestHandler<ListPlatformAuditLogsQuery, Result<PlatformAuditLogListResponse>>
{
    public const int MaxPageSize = 100;

    private readonly IPlatformAuditLogRepository _auditLogs;
    private readonly IPlatformUserRepository _users;

    public ListPlatformAuditLogsQueryHandler(IPlatformAuditLogRepository auditLogs, IPlatformUserRepository users)
    {
        _auditLogs = auditLogs;
        _users = users;
    }

    public async Task<Result<PlatformAuditLogListResponse>> Handle(
        ListPlatformAuditLogsQuery request,
        CancellationToken ct)
    {
        if (request.From is { } from && request.To is { } to && from > to)
            return Result<PlatformAuditLogListResponse>.Failure("'from' must be on or before 'to'.");

        if (request.Outcome is { Length: > 0 } outcome
            && outcome != PlatformAuditLog.OutcomeSucceeded
            && outcome != PlatformAuditLog.OutcomeFailed)
        {
            return Result<PlatformAuditLogListResponse>.Failure(
                $"'outcome' must be '{PlatformAuditLog.OutcomeSucceeded}' or '{PlatformAuditLog.OutcomeFailed}'.");
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var filter = new PlatformAuditLogFilter(
            request.ActorPlatformUserId,
            Blank(request.Action),
            Blank(request.TargetType),
            Blank(request.TargetId),
            request.TargetTenantId,
            Blank(request.Outcome),
            request.From,
            request.To);

        var (items, total) = await _auditLogs.ListAsync(filter, page, pageSize, ct);

        var users = await _users.ListUsersAsync(ct);
        var usersById = users.ToDictionary(u => u.Id);

        var rows = items
            .Select(e =>
            {
                var actor = e.ActorPlatformUserId is Guid id && usersById.TryGetValue(id, out var u) ? u : null;
                return new PlatformAuditLogEntryResponse(
                    e.Id,
                    e.CreatedAt,
                    e.ActorPlatformUserId,
                    actor?.Email,
                    actor?.FullName,
                    e.Action,
                    e.TargetType,
                    e.TargetId,
                    e.TargetTenantId,
                    e.Outcome,
                    e.FailureReason,
                    e.BeforeJson,
                    e.AfterJson,
                    e.IpAddress,
                    e.UserAgent,
                    e.CorrelationId);
            })
            .ToList();

        return Result<PlatformAuditLogListResponse>.Success(
            new PlatformAuditLogListResponse(rows, total, page, pageSize));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
