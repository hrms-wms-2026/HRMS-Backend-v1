using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Behaviors;

public class AuditingBehaviorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPlatformAuditLogRepository> _platformAuditLogs = new();
    private readonly Mock<IAuditLogRepository> _tenantAuditLogs = new();
    private readonly Mock<ICurrentPlatformUserContext> _platformUser = new();
    private readonly Mock<ICurrentUser> _tenantUser = new();
    private readonly Mock<IRequestAuditContext> _requestContext = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ILogger<AuditingBehavior<PlatformCommand, Result>>> _logger = new();
    private readonly AuditTrail _auditTrail = new();

    private readonly List<PlatformAuditLog> _platformRows = new();
    private readonly List<AuditLog> _tenantRows = new();
    private readonly List<string> _calls = new();

    private readonly Guid _actorId = Guid.NewGuid();

    public record PlatformCommand(Guid RoleId, string Secret) : IRequest<Result>, IPlatformAuditedCommand<Result>
    {
        public AuditEntry DescribeAudit(Result response) => new()
        {
            Action = "platform_role.updated",
            TargetType = "platform_role",
            TargetId = RoleId.ToString(),
            After = new { secretRotated = true },
        };
    }

    public record UnitPlatformCommand(Guid TenantId) : IRequest, IPlatformAuditedCommand<MediatR.Unit>
    {
        public AuditEntry DescribeAudit(MediatR.Unit response) => new()
        {
            Action = "tenant_session.revoked",
            TargetType = "tenant_session",
            TargetTenantId = TenantId,
        };
    }

    public record LeakyCommand(string Secret) : IRequest<Result>, IPlatformAuditedCommand<Result>
    {
        public AuditEntry DescribeAudit(Result response) => new()
        {
            Action = "leaky.done",
            TargetType = "leaky",
            After = this,
        };
    }

    public record TenantCommand(Guid RoleId) : IRequest<Result<string>>, ITenantAuditedCommand<Result<string>>
    {
        public AuditEntry DescribeAudit(Result<string> response) => new()
        {
            Action = "role.permissions_assigned",
            TargetType = "role",
            TargetId = RoleId.ToString(),
            After = new { permissionIds = new[] { "p1", "p2" } },
        };
    }

    public record PlainCommand : IRequest<Result>;

    public AuditingBehaviorTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _platformUser.Setup(p => p.UserId).Returns(_actorId);
        _requestContext.Setup(r => r.IpAddress).Returns("203.0.113.7");
        _requestContext.Setup(r => r.UserAgent).Returns("unit-test-agent");
        _requestContext.Setup(r => r.CorrelationId).Returns("corr-123");

        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>(async (op, ct) =>
            {
                _calls.Add("tx:begin");
                var result = await op(ct);
                _calls.Add("tx:commit");
                return result;
            });
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<MediatR.Unit>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<MediatR.Unit>>, CancellationToken>(async (op, ct) =>
            {
                _calls.Add("tx:begin");
                var result = await op(ct);
                _calls.Add("tx:commit");
                return result;
            });
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<string>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<string>>>, CancellationToken>(async (op, ct) =>
            {
                _calls.Add("tx:begin");
                var result = await op(ct);
                _calls.Add("tx:commit");
                return result;
            });
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("save"))
            .ReturnsAsync(1);

        _platformAuditLogs
            .Setup(r => r.AddAsync(It.IsAny<PlatformAuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformAuditLog, CancellationToken>((row, _) => { _platformRows.Add(row); _calls.Add("audit:add"); })
            .Returns(Task.CompletedTask);
        _tenantAuditLogs
            .Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((row, _) => { _tenantRows.Add(row); _calls.Add("audit:add"); })
            .Returns(Task.CompletedTask);
    }

    private AuditingBehavior<TRequest, TResponse> Build<TRequest, TResponse>()
        where TRequest : notnull =>
        new(
            _unitOfWork.Object,
            _platformAuditLogs.Object,
            _tenantAuditLogs.Object,
            _platformUser.Object,
            _tenantUser.Object,
            _requestContext.Object,
            _auditTrail,
            _clock.Object,
            new Mock<ILogger<AuditingBehavior<TRequest, TResponse>>>().Object);

    [Fact]
    public async Task SuccessfulPlatformCommand_WritesOneRowWithActorActionTargetAndRequestMetadata()
    {
        var roleId = Guid.NewGuid();
        var behavior = Build<PlatformCommand, Result>();

        var result = await behavior.Handle(
            new PlatformCommand(roleId, "irrelevant"),
            _ => { _calls.Add("handler"); return Task.FromResult(Result.Success()); },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _platformRows.Should().ContainSingle();
        var row = _platformRows[0];
        row.Id.Should().NotBe(Guid.Empty);
        row.ActorPlatformUserId.Should().Be(_actorId);
        row.Action.Should().Be("platform_role.updated");
        row.TargetType.Should().Be("platform_role");
        row.TargetId.Should().Be(roleId.ToString());
        row.TargetTenantId.Should().BeNull();
        row.IpAddress.Should().Be("203.0.113.7");
        row.UserAgent.Should().Be("unit-test-agent");
        row.CorrelationId.Should().Be("corr-123");
        row.CreatedAt.Should().Be(Now);
        row.BeforeJson.Should().BeNull();
        JsonDocument.Parse(row.AfterJson!).RootElement.GetProperty("secretRotated").GetBoolean().Should().BeTrue();
        _tenantRows.Should().BeEmpty();
    }

    [Fact]
    public async Task SuccessfulCommand_WritesAuditRowInsideTheSameTransactionAsTheChange()
    {
        var behavior = Build<PlatformCommand, Result>();

        await behavior.Handle(
            new PlatformCommand(Guid.NewGuid(), "x"),
            _ => { _calls.Add("handler"); return Task.FromResult(Result.Success()); },
            CancellationToken.None);

        _calls.Should().Equal("tx:begin", "handler", "audit:add", "save", "tx:commit");
    }

    [Fact]
    public async Task FailedResult_WritesNoAuditRow()
    {
        var behavior = Build<PlatformCommand, Result>();

        var result = await behavior.Handle(
            new PlatformCommand(Guid.NewGuid(), "x"),
            _ => Task.FromResult(Result.Forbidden()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _platformRows.Should().BeEmpty();
        _tenantRows.Should().BeEmpty();
        _calls.Should().NotContain("save");
    }

    [Fact]
    public async Task HandlerException_WritesNoAuditRowAndPropagates()
    {
        var behavior = Build<UnitPlatformCommand, MediatR.Unit>();

        var act = () => behavior.Handle(
            new UnitPlatformCommand(Guid.NewGuid()),
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _platformRows.Should().BeEmpty();
    }

    [Fact]
    public async Task UnitCommand_WithoutException_IsTreatedAsSuccess()
    {
        var tenantId = Guid.NewGuid();
        var behavior = Build<UnitPlatformCommand, MediatR.Unit>();

        await behavior.Handle(new UnitPlatformCommand(tenantId), _ => Task.FromResult(MediatR.Unit.Value), CancellationToken.None);

        _platformRows.Should().ContainSingle().Which.TargetTenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task BeforeState_RecordedByTheHandler_IsStoredAsBeforeJson()
    {
        var behavior = Build<PlatformCommand, Result>();

        await behavior.Handle(
            new PlatformCommand(Guid.NewGuid(), "x"),
            _ =>
            {
                _auditTrail.RecordBefore(new { permissions = new[] { "a.read" } });
                return Task.FromResult(Result.Success());
            },
            CancellationToken.None);

        var before = JsonDocument.Parse(_platformRows.Single().BeforeJson!).RootElement;
        before.GetProperty("permissions")[0].GetString().Should().Be("a.read");
    }

    [Fact]
    public async Task TargetId_RecordedByTheHandler_IsUsedWhenTheEntryHasNone()
    {
        var behavior = Build<UnitPlatformCommand, MediatR.Unit>();

        await behavior.Handle(
            new UnitPlatformCommand(Guid.NewGuid()),
            _ =>
            {
                _auditTrail.RecordTarget("created-id-42");
                return Task.FromResult(MediatR.Unit.Value);
            },
            CancellationToken.None);

        _platformRows.Single().TargetId.Should().Be("created-id-42");
    }

    [Fact]
    public async Task BeforeState_RecordedOutsideAnAuditedCommand_IsIgnored()
    {
        _auditTrail.RecordBefore(new { stale = true });
        var behavior = Build<PlatformCommand, Result>();

        await behavior.Handle(new PlatformCommand(Guid.NewGuid(), "x"), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        _platformRows.Single().BeforeJson.Should().BeNull();
    }

    [Fact]
    public async Task TenantCommand_WritesToTenantAuditLogWithCurrentUserAndTenant()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tenantUser.Setup(u => u.IsAuthenticated).Returns(true);
        _tenantUser.Setup(u => u.TenantId).Returns(tenantId);
        _tenantUser.Setup(u => u.UserId).Returns(userId);
        _platformUser.Setup(p => p.UserId).Returns((Guid?)null);
        var roleId = Guid.NewGuid();
        var behavior = Build<TenantCommand, Result<string>>();

        await behavior.Handle(new TenantCommand(roleId), _ => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);

        _platformRows.Should().BeEmpty();
        var row = _tenantRows.Should().ContainSingle().Subject;
        row.TenantId.Should().Be(tenantId);
        row.UserId.Should().Be(userId);
        row.Action.Should().Be("role.permissions_assigned");
        row.ResourceType.Should().Be("role");
        row.ResourceId.Should().Be(roleId);
        row.IpAddress.Should().Be("203.0.113.7");
        row.NewValuesJson.Should().Contain("p2");
    }

    [Fact]
    public async Task TenantCommand_WithoutTenant_FailsClosedInsteadOfLosingTheAuditRow()
    {
        _tenantUser.Setup(u => u.IsAuthenticated).Returns(false);
        _tenantUser.Setup(u => u.TenantId).Returns(Guid.Empty);
        var behavior = Build<TenantCommand, Result<string>>();

        var act = () => behavior.Handle(new TenantCommand(Guid.NewGuid()), _ => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _tenantRows.Should().BeEmpty();
        _calls.Should().NotContain("tx:commit");
    }

    [Fact]
    public async Task ProjectionThatIsTheCommandItself_IsRejected_SoNoSecretIsSerialized()
    {
        const string fakeSecret = "FAKE-SECRET-do-not-log-4f1c";
        var behavior = Build<LeakyCommand, Result>();

        var act = () => behavior.Handle(new LeakyCommand(fakeSecret), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _platformRows.Should().BeEmpty();
        _calls.Should().NotContain("tx:commit");
    }

    [Fact]
    public async Task AuditWriteFailure_PropagatesSoTheTransactionRollsBack()
    {
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var behavior = Build<PlatformCommand, Result>();

        var act = () => behavior.Handle(new PlatformCommand(Guid.NewGuid(), "x"), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db down");
        _calls.Should().NotContain("tx:commit");
    }

    [Fact]
    public async Task NonAuditedRequest_PassesThroughWithoutTransactionOrRow()
    {
        var behavior = Build<PlainCommand, Result>();

        var result = await behavior.Handle(new PlainCommand(), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _platformRows.Should().BeEmpty();
        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
