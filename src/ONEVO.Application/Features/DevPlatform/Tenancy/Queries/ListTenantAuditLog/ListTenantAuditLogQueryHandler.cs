using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenantAuditLog;

public sealed class ListTenantAuditLogQueryHandler
    : IRequestHandler<ListTenantAuditLogQuery, Result<TenantAuditLogListResponse>>
{
    private readonly ITenantRepository _tenants;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IUserRepository _users;

    public ListTenantAuditLogQueryHandler(
        ITenantRepository tenants,
        IAuditLogRepository auditLogs,
        IUserRepository users)
    {
        _tenants = tenants;
        _auditLogs = auditLogs;
        _users = users;
    }

    public async Task<Result<TenantAuditLogListResponse>> Handle(
        ListTenantAuditLogQuery request,
        CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(request.TenantId, ct) is null)
            return Result<TenantAuditLogListResponse>.NotFound("Tenant not found.");

        var (entries, total) = await _auditLogs.ListByTenantIdAsync(
            request.TenantId, request.Page, request.PageSize, ct);

        var responses = new List<TenantAuditLogEntryResponse>(entries.Count);
        foreach (var entry in entries)
        {
            string? userEmail = null;
            if (entry.UserId is Guid userId)
            {
                var user = await _users.GetByIdAsync(userId, ct);
                userEmail = user?.Email;
            }

            responses.Add(new TenantAuditLogEntryResponse(
                entry.Id,
                entry.UserId,
                userEmail,
                entry.Action,
                entry.ResourceType,
                entry.ResourceId,
                entry.IpAddress,
                entry.CreatedAt));
        }

        return Result<TenantAuditLogListResponse>.Success(
            new TenantAuditLogListResponse(responses, total, request.Page, request.PageSize));
    }
}
