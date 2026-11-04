using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Audit.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Audit.Queries.ListPlatformAuditLog;

public sealed class ListPlatformAuditLogQueryHandler
    : IRequestHandler<ListPlatformAuditLogQuery, Result<PlatformAuditLogListResponse>>
{
    private const int MaxPageSize = 100;

    private readonly IPlatformAuditLogRepository _auditLogs;
    private readonly IPlatformUserRepository _users;

    public ListPlatformAuditLogQueryHandler(IPlatformAuditLogRepository auditLogs, IPlatformUserRepository users)
    {
        _auditLogs = auditLogs;
        _users = users;
    }

    public async Task<Result<PlatformAuditLogListResponse>> Handle(ListPlatformAuditLogQuery request, CancellationToken ct)
    {
        if (request.From is { } from && request.To is { } to && from > to)
            return Result<PlatformAuditLogListResponse>.Failure("from must not be after to.", 400);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var filter = new PlatformAuditLogFilter(
            request.ActorPlatformUserId,
            Normalize(request.Action),
            Normalize(request.TargetType),
            Normalize(request.TargetId),
            request.TargetTenantId,
            request.From,
            request.To);

        var (rows, total) = await _auditLogs.ListAsync(filter, page, pageSize, ct);

        var emails = new Dictionary<Guid, string?>();
        var items = new List<PlatformAuditLogEntryResponse>(rows.Count);
        foreach (var row in rows)
        {
            string? actorEmail = null;
            if (row.ActorPlatformUserId is { } actorId)
            {
                if (!emails.TryGetValue(actorId, out actorEmail))
                {
                    actorEmail = (await _users.GetByIdAsync(actorId, ct))?.Email;
                    emails[actorId] = actorEmail;
                }
            }

            items.Add(new PlatformAuditLogEntryResponse(
                row.Id,
                row.ActorPlatformUserId,
                actorEmail,
                row.Action,
                row.TargetType,
                row.TargetId,
                row.TargetTenantId,
                Parse(row.BeforeJson),
                Parse(row.AfterJson),
                row.IpAddress,
                row.UserAgent,
                row.CorrelationId,
                row.CreatedAt));
        }

        return Result<PlatformAuditLogListResponse>.Success(
            new PlatformAuditLogListResponse(items, total, page, pageSize));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
