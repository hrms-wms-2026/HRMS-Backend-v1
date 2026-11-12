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
/// Writes the privileged-action audit trail (security audit finding F-07) for every command that
/// implements <see cref="IAuditedCommand{TResponse}"/>.
///
/// Atomicity: the handler and the audit row run in one database transaction. If the audit row
/// cannot be written, the transaction rolls back and the command fails, so a privileged change
/// can never be committed without its audit row. Side effects outside the database (an e-mail, a
/// call to a payment provider) cannot be rolled back and may already have happened.
///
/// Where the row goes:
/// - a platform admin request (or an unauthenticated system caller such as tray release ingest)
///   writes <c>platform_audit_logs</c>, for success and for failure;
/// - a tenant user request writes the tenant's own <c>audit_logs</c>, for success only.
///
/// A failed attempt is written after the transaction rolled back, in its own save, with the
/// handler's unsaved changes discarded first. If that write fails it is logged at Error level and
/// the original failure is still returned.
/// </summary>
public sealed class AuditingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int ActionMaxLength = 100;
    private const int TargetTypeMaxLength = 80;
    private const int TenantResourceTypeMaxLength = 50;
    private const int TargetIdMaxLength = 200;
    private const int FailureReasonMaxLength = 200;
    private const int IpAddressMaxLength = 45;
    private const int UserAgentMaxLength = 512;
    private const int CorrelationIdMaxLength = 100;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPlatformAuditLogRepository _platformAuditLogs;
    private readonly IAuditLogRepository _tenantAuditLogs;
    private readonly ICurrentPlatformUserContext _platformUser;
    private readonly ICurrentUser _tenantUser;
    private readonly IRequestClientInfo _client;
    private readonly IDateTimeProvider _clock;
    private readonly IEnumerable<IAuditStateReader<TRequest>> _stateReaders;
    private readonly ILogger<AuditingBehavior<TRequest, TResponse>> _logger;

    public AuditingBehavior(
        IUnitOfWork unitOfWork,
        IPlatformAuditLogRepository platformAuditLogs,
        IAuditLogRepository tenantAuditLogs,
        ICurrentPlatformUserContext platformUser,
        ICurrentUser tenantUser,
        IRequestClientInfo client,
        IDateTimeProvider clock,
        IEnumerable<IAuditStateReader<TRequest>> stateReaders,
        ILogger<AuditingBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _platformAuditLogs = platformAuditLogs;
        _tenantAuditLogs = tenantAuditLogs;
        _platformUser = platformUser;
        _tenantUser = tenantUser;
        _client = client;
        _clock = clock;
        _stateReaders = stateReaders;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IAuditedCommand<TResponse> audited)
            return await next(ct);

        var platformActorId = _platformUser.UserId;
        var tenantScope = platformActorId is null
            && _tenantUser.IsAuthenticated
            && _tenantUser.TenantId != Guid.Empty;
        var reader = _stateReaders.FirstOrDefault();

        TResponse response;
        try
        {
            response = await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var before = reader is null ? null : await reader.ReadAsync(request, innerCt);

                var result = await next(innerCt);
                if (!IsSuccess(result))
                    return result;

                var entry = audited.DescribeAudit(result);
                var after = reader is null ? entry.Details : await reader.ReadAsync(request, innerCt);

                if (tenantScope)
                    await AddTenantRowAsync(entry, before, after, innerCt);
                else
                    await AddPlatformRowAsync(platformActorId, entry, PlatformAuditLog.OutcomeSucceeded, null, before, after, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);
                return result;
            }, ct);
        }
        catch (Exception ex) when (!tenantScope && !(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            await TryRecordFailureAsync(platformActorId, audited, default, ex.GetType().Name);
            throw;
        }

        if (!tenantScope && response is IResultStatus { IsSuccess: false } failed)
        {
            var reason = failed.ErrorCode is { Length: > 0 } code
                ? $"{failed.StatusCode}:{code}"
                : failed.StatusCode?.ToString() ?? "failed";
            await TryRecordFailureAsync(platformActorId, audited, response, reason);
        }

        return response;
    }

    private static bool IsSuccess(TResponse response) => response is not IResultStatus { IsSuccess: false };

    private async Task TryRecordFailureAsync(
        Guid? actorId,
        IAuditedCommand<TResponse> audited,
        TResponse? response,
        string reason)
    {
        try
        {
            // The handler's unsaved changes belong to a failed command: never let this save flush them.
            _unitOfWork.ClearTracking();
            var entry = audited.DescribeAudit(response);
            await AddPlatformRowAsync(actorId, entry, PlatformAuditLog.OutcomeFailed, reason, null, entry.Details, CancellationToken.None);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception writeEx)
        {
            _logger.LogError(
                writeEx,
                "Could not write the failed-attempt audit row for {Command} (actor {ActorId}, reason {Reason})",
                typeof(TRequest).Name,
                actorId,
                reason);
        }
    }

    private Task AddPlatformRowAsync(
        Guid? actorId,
        AuditEntry entry,
        string outcome,
        string? failureReason,
        object? before,
        object? after,
        CancellationToken ct)
        => _platformAuditLogs.AddAsync(new PlatformAuditLog
        {
            Id = Guid.NewGuid(),
            ActorPlatformUserId = actorId,
            Action = Truncate(entry.Action, ActionMaxLength)!,
            TargetType = Truncate(entry.TargetType, TargetTypeMaxLength)!,
            TargetId = Truncate(entry.TargetId, TargetIdMaxLength),
            TargetTenantId = entry.TargetTenantId,
            Outcome = outcome,
            FailureReason = Truncate(failureReason, FailureReasonMaxLength),
            BeforeJson = AuditJson.Serialize(before),
            AfterJson = AuditJson.Serialize(after),
            IpAddress = Truncate(_client.IpAddress, IpAddressMaxLength),
            UserAgent = Truncate(_client.UserAgent, UserAgentMaxLength),
            CorrelationId = Truncate(_client.CorrelationId, CorrelationIdMaxLength),
            CreatedAt = _clock.UtcNow,
        }, ct);

    private Task AddTenantRowAsync(AuditEntry entry, object? before, object? after, CancellationToken ct)
        => _tenantAuditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantUser.TenantId,
            UserId = _tenantUser.UserId == Guid.Empty ? null : _tenantUser.UserId,
            Action = Truncate(entry.Action, ActionMaxLength)!,
            ResourceType = Truncate(entry.TargetType, TenantResourceTypeMaxLength)!,
            ResourceId = Guid.TryParse(entry.TargetId, out var resourceId) ? resourceId : null,
            OldValuesJson = AuditJson.Serialize(before),
            NewValuesJson = AuditJson.Serialize(after),
            IpAddress = Truncate(_client.IpAddress, IpAddressMaxLength),
            CorrelationId = Guid.TryParse(_client.CorrelationId, out var correlationId) ? correlationId : null,
            CreatedAt = _clock.UtcNow,
        }, ct);

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
