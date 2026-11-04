using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Application.Common.Auditing;

/// <summary>
/// Runs a real command through <see cref="AuditingBehavior{TRequest,TResponse}"/> with a stub
/// handler and captures the audit rows it writes.
/// </summary>
public sealed class AuditingHarness
{
    public Guid ActorPlatformUserId { get; } = Guid.NewGuid();
    public Guid TenantId { get; } = Guid.NewGuid();
    public Guid TenantUserId { get; } = Guid.NewGuid();
    public AuditTrail AuditTrail { get; } = new();
    public List<PlatformAuditLog> PlatformRows { get; } = new();
    public List<AuditLog> TenantRows { get; } = new();

    public async Task<TResponse> RunAsync<TRequest, TResponse>(
        TRequest request,
        TResponse response,
        Action<IAuditTrail>? duringHandler = null)
        where TRequest : notnull
    {
        var platformRepo = new Mock<IPlatformAuditLogRepository>();
        platformRepo.Setup(r => r.AddAsync(It.IsAny<PlatformAuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformAuditLog, CancellationToken>((row, _) => PlatformRows.Add(row))
            .Returns(Task.CompletedTask);
        var tenantRepo = new Mock<IAuditLogRepository>();
        tenantRepo.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((row, _) => TenantRows.Add(row))
            .Returns(Task.CompletedTask);

        var platformUser = new Mock<ICurrentPlatformUserContext>();
        platformUser.Setup(p => p.UserId).Returns(ActorPlatformUserId);
        var tenantUser = new Mock<ICurrentUser>();
        tenantUser.Setup(u => u.IsAuthenticated).Returns(true);
        tenantUser.Setup(u => u.TenantId).Returns(TenantId);
        tenantUser.Setup(u => u.UserId).Returns(TenantUserId);
        var requestContext = new Mock<IRequestAuditContext>();

        var behavior = new AuditingBehavior<TRequest, TResponse>(
            new FakeUnitOfWork(),
            platformRepo.Object,
            tenantRepo.Object,
            platformUser.Object,
            tenantUser.Object,
            requestContext.Object,
            AuditTrail,
            new FakeDateTimeProvider(),
            NullLogger<AuditingBehavior<TRequest, TResponse>>.Instance);

        return await behavior.Handle(
            request,
            _ =>
            {
                duringHandler?.Invoke(AuditTrail);
                return Task.FromResult(response);
            },
            CancellationToken.None);
    }

    /// <summary>Every row as JSON, including before/after payloads, for "contains no X" assertions.</summary>
    public string AllRowsJson() =>
        JsonSerializer.Serialize(new { platform = PlatformRows, tenant = TenantRows });
}
