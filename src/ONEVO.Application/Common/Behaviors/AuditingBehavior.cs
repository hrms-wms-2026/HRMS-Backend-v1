using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;

namespace ONEVO.Application.Common.Behaviors;

/// <summary>
/// Writes the audit row for an <see cref="IAuditedCommand"/> in the same database transaction as
/// the change: the handler runs inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>
/// (re-entrant, so handlers that open their own transaction join this one), then the row is added
/// and saved before commit. If the row cannot be written the exception propagates and the change
/// rolls back with it, so a privileged change never commits without its audit row.
/// A failed <see cref="IResultStatus"/> or an exception writes no row.
/// </summary>
public class AuditingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPlatformAuditLogRepository _platformAuditLogs;
    private readonly IAuditLogRepository _tenantAuditLogs;
    private readonly ICurrentPlatformUserContext _currentPlatformUser;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestAuditContext _requestContext;
    private readonly AuditTrail _auditTrail;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuditingBehavior<TRequest, TResponse>> _logger;

    public AuditingBehavior(
        IUnitOfWork unitOfWork,
        IPlatformAuditLogRepository platformAuditLogs,
        IAuditLogRepository tenantAuditLogs,
        ICurrentPlatformUserContext currentPlatformUser,
        ICurrentUser currentUser,
        IRequestAuditContext requestContext,
        AuditTrail auditTrail,
        IDateTimeProvider clock,
        ILogger<AuditingBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _platformAuditLogs = platformAuditLogs;
        _tenantAuditLogs = tenantAuditLogs;
        _currentPlatformUser = currentPlatformUser;
        _currentUser = currentUser;
        _requestContext = requestContext;
        _auditTrail = auditTrail;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IAuditedCommand<TResponse> audited)
            return await next(ct);

        using var frame = _auditTrail.BeginFrame();

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // A retried transaction re-runs the handler, which records its before-state again.
            frame.Before = null;
            frame.TargetId = null;

            var response = await next(innerCt);
            if (response is IResultStatus { IsSuccess: false })
                return response;

            try
            {
                var entry = audited.DescribeAudit(response);
                if (entry.TargetId is null && frame.TargetId is not null)
                    entry = entry with { TargetId = frame.TargetId };
                await WriteAsync(audited, entry, frame.Before, innerCt);
                await _unitOfWork.SaveChangesAsync(innerCt);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Audit row for {Command} could not be written; the change is rolled back.",
                    typeof(TRequest).Name);
                throw;
            }

            return response;
        }, ct);
    }

    private Task WriteAsync(IAuditedCommand<TResponse> audited, AuditEntry entry, object? before, CancellationToken ct)
    {
        var beforeJson = Serialize(before);
        var afterJson = Serialize(entry.After);

        switch (audited)
        {
            case IPlatformAuditedCommand<TResponse>:
                return _platformAuditLogs.AddAsync(new PlatformAuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorPlatformUserId = _currentPlatformUser.UserId,
                    Action = entry.Action,
                    TargetType = entry.TargetType,
                    TargetId = entry.TargetId,
                    TargetTenantId = entry.TargetTenantId,
                    BeforeJson = beforeJson,
                    AfterJson = afterJson,
                    IpAddress = _requestContext.IpAddress,
                    UserAgent = _requestContext.UserAgent,
                    CorrelationId = _requestContext.CorrelationId,
                    CreatedAt = _clock.UtcNow,
                }, ct);

            case ITenantAuditedCommand<TResponse>:
                var tenantId = entry.TargetTenantId ?? _currentUser.TenantId;
                if (tenantId == Guid.Empty)
                    throw new InvalidOperationException(
                        $"{typeof(TRequest).Name} is tenant-audited but no tenant is in context.");

                return _tenantAuditLogs.AddAsync(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
                    Action = entry.Action,
                    ResourceType = entry.TargetType,
                    ResourceId = Guid.TryParse(entry.TargetId, out var resourceId) ? resourceId : null,
                    OldValuesJson = beforeJson,
                    NewValuesJson = afterJson,
                    IpAddress = _requestContext.IpAddress,
                    CorrelationId = Guid.TryParse(_requestContext.CorrelationId, out var correlationId) ? correlationId : null,
                    CreatedAt = _clock.UtcNow,
                }, ct);

            default:
                throw new InvalidOperationException(
                    $"{typeof(TRequest).Name} must implement IPlatformAuditedCommand<> or ITenantAuditedCommand<>.");
        }
    }

    private static string? Serialize(object? projection)
    {
        if (projection is null)
            return null;

        // Guard against the one mistake that leaks secrets: handing over the command (which can carry
        // a credential) or a domain entity instead of an explicit projection.
        var type = projection.GetType();
        if (projection is IBaseRequest || type.Assembly == typeof(AuditLog).Assembly)
            throw new InvalidOperationException(
                $"Audit projection for {typeof(TRequest).Name} must be an explicit projection, not {type.Name}.");

        return JsonSerializer.Serialize(projection, type, JsonOptions);
    }
}
