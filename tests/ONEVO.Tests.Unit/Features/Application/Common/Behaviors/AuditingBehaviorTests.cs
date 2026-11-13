using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.Exceptions;
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
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 30, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentPlatformUserContext> _platformUser = new();
    private readonly Mock<ICurrentUser> _tenantUser = new();
    private readonly Mock<IRequestClientInfo> _client = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakePlatformAuditLogs _platformRows = new();
    private readonly FakeTenantAuditLogs _tenantRows = new();

    public AuditingBehaviorTests()
    {
        _client.Setup(c => c.IpAddress).Returns("203.0.113.7");
        _client.Setup(c => c.UserAgent).Returns("Mozilla/5.0 test");
        _client.Setup(c => c.CorrelationId).Returns("8f6f1c0e-3c43-4a4c-9a55-31f1b0d1c0aa");
        _clock.Setup(c => c.UtcNow).Returns(Now);
    }

    public sealed record AuditedCommand(Guid TargetId, string Name)
        : IRequest<Result<string>>, IAuditedCommand<Result<string>>
    {
        public AuditEntry DescribeAudit(Result<string>? response)
            => AuditEntry.For("thing.updated", "thing", TargetId, null, new { Name, Echo = response?.Value });
    }

    public sealed record AuditedVoidCommand(Guid TargetId) : IRequest, IAuditedCommand<MediatR.Unit>
    {
        public AuditEntry DescribeAudit(MediatR.Unit response) => AuditEntry.For("thing.touched", "thing", TargetId);
    }

    public sealed record PlainCommand : IRequest<Result<string>>;

    private AuditingBehavior<TRequest, TResponse> Build<TRequest, TResponse>(
        params IAuditStateReader<TRequest>[] readers)
        where TRequest : notnull
        => new(
            _unitOfWork,
            _platformRows,
            _tenantRows,
            _platformUser.Object,
            _tenantUser.Object,
            _client.Object,
            _clock.Object,
            readers,
            NullLogger<AuditingBehavior<TRequest, TResponse>>.Instance);

    private Guid AsPlatformAdmin()
    {
        var id = Guid.NewGuid();
        _platformUser.Setup(p => p.UserId).Returns(id);
        return id;
    }

    [Fact]
    public async Task Successful_audited_command_writes_one_row_with_actor_action_target_and_client_details()
    {
        var actorId = AsPlatformAdmin();
        var targetId = Guid.NewGuid();

        var response = await Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(targetId, "Payroll"),
            _ => Task.FromResult(Result<string>.Success("done")),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var row = _platformRows.Rows.Should().ContainSingle().Subject;
        row.ActorPlatformUserId.Should().Be(actorId);
        row.Action.Should().Be("thing.updated");
        row.TargetType.Should().Be("thing");
        row.TargetId.Should().Be(targetId.ToString());
        row.Outcome.Should().Be(PlatformAuditLog.OutcomeSucceeded);
        row.FailureReason.Should().BeNull();
        row.IpAddress.Should().Be("203.0.113.7");
        row.UserAgent.Should().Be("Mozilla/5.0 test");
        row.CorrelationId.Should().Be("8f6f1c0e-3c43-4a4c-9a55-31f1b0d1c0aa");
        row.CreatedAt.Should().Be(Now);
        JsonDocument.Parse(row.AfterJson!).RootElement.GetProperty("name").GetString().Should().Be("Payroll");
        JsonDocument.Parse(row.AfterJson!).RootElement.GetProperty("echo").GetString().Should().Be("done");
        _tenantRows.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Audit_row_is_saved_inside_the_handlers_transaction()
    {
        AsPlatformAdmin();

        await Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(Guid.NewGuid(), "x"),
            _ =>
            {
                _unitOfWork.InTransaction.Should().BeTrue("the handler must run inside the audit transaction");
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        _unitOfWork.TransactionCount.Should().Be(1);
        _unitOfWork.SavesInsideTransaction.Should().Be(1);
        _unitOfWork.SavesOutsideTransaction.Should().Be(0);
    }

    [Fact]
    public async Task Void_command_is_audited_and_its_pending_changes_are_saved()
    {
        // UpdatePlatformRolePermissions and friends never call SaveChanges themselves; the audit
        // save is what persists their tracked changes.
        AsPlatformAdmin();
        var targetId = Guid.NewGuid();

        await Build<AuditedVoidCommand, MediatR.Unit>().Handle(
            new AuditedVoidCommand(targetId),
            _ => Task.FromResult(MediatR.Unit.Value),
            CancellationToken.None);

        _platformRows.Rows.Should().ContainSingle(r => r.Action == "thing.touched" && r.TargetId == targetId.ToString());
        _unitOfWork.SavesInsideTransaction.Should().Be(1);
    }

    [Fact]
    public async Task Failed_result_writes_no_succeeded_row_but_records_the_failed_attempt()
    {
        AsPlatformAdmin();

        var response = await Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(Guid.NewGuid(), "x"),
            _ => Task.FromResult(Result<string>.Failure("nope", 409, "slug_taken")),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        _platformRows.Rows.Should().NotContain(r => r.Outcome == PlatformAuditLog.OutcomeSucceeded);
        var row = _platformRows.Rows.Should().ContainSingle().Subject;
        row.Outcome.Should().Be(PlatformAuditLog.OutcomeFailed);
        row.FailureReason.Should().Be("409:slug_taken");
        _unitOfWork.SavesInsideTransaction.Should().Be(0, "a failed command's pending changes must not be flushed");
        _unitOfWork.ClearedBeforeFailureSave.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_exception_records_a_failed_attempt_and_is_rethrown()
    {
        AsPlatformAdmin();

        var act = () => Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(Guid.NewGuid(), "x"),
            _ => throw new NotFoundException("missing"),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        var row = _platformRows.Rows.Should().ContainSingle().Subject;
        row.Outcome.Should().Be(PlatformAuditLog.OutcomeFailed);
        row.FailureReason.Should().Be(nameof(NotFoundException));
        row.FailureReason.Should().NotContain("missing", "exception messages are never stored");
    }

    [Fact]
    public async Task Audit_write_failure_rolls_back_the_command()
    {
        AsPlatformAdmin();
        _platformRows.ThrowOnAdd = true;

        var act = () => Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(Guid.NewGuid(), "x"),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _unitOfWork.CommittedTransactions.Should().Be(0);
    }

    [Fact]
    public async Task Request_without_the_marker_is_not_audited_and_runs_without_a_transaction()
    {
        AsPlatformAdmin();

        await Build<PlainCommand, Result<string>>().Handle(
            new PlainCommand(),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _platformRows.Rows.Should().BeEmpty();
        _unitOfWork.TransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task Tenant_user_command_writes_the_tenant_audit_log_on_success_only()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _platformUser.Setup(p => p.UserId).Returns((Guid?)null);
        _tenantUser.Setup(u => u.IsAuthenticated).Returns(true);
        _tenantUser.Setup(u => u.TenantId).Returns(tenantId);
        _tenantUser.Setup(u => u.UserId).Returns(userId);
        var targetId = Guid.NewGuid();

        await Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(targetId, "Managers"),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);
        await Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(targetId, "Managers"),
            _ => Task.FromResult(Result<string>.Failure("nope")),
            CancellationToken.None);

        _platformRows.Rows.Should().BeEmpty();
        var row = _tenantRows.Rows.Should().ContainSingle().Subject;
        row.TenantId.Should().Be(tenantId);
        row.UserId.Should().Be(userId);
        row.Action.Should().Be("thing.updated");
        row.ResourceId.Should().Be(targetId);
        row.CorrelationId.Should().Be(Guid.Parse("8f6f1c0e-3c43-4a4c-9a55-31f1b0d1c0aa"));
    }

    [Fact]
    public async Task System_caller_without_a_user_is_recorded_with_no_actor()
    {
        _platformUser.Setup(p => p.UserId).Returns((Guid?)null);
        _tenantUser.Setup(u => u.IsAuthenticated).Returns(false);

        await Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(Guid.NewGuid(), "ingest"),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _platformRows.Rows.Should().ContainSingle(r => r.ActorPlatformUserId == null);
    }

    [Fact]
    public async Task State_reader_supplies_before_and_after_values()
    {
        AsPlatformAdmin();
        var state = "old";
        var reader = new Mock<IAuditStateReader<AuditedCommand>>();
        reader.Setup(r => r.ReadAsync(It.IsAny<AuditedCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new { Value = state });

        await Build<AuditedCommand, Result<string>>(reader.Object).Handle(
            new AuditedCommand(Guid.NewGuid(), "x"),
            _ =>
            {
                state = "new";
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        var row = _platformRows.Rows.Should().ContainSingle().Subject;
        row.BeforeJson.Should().Be("{\"value\":\"old\"}");
        row.AfterJson.Should().Be("{\"value\":\"new\"}");
    }

    [Fact]
    public async Task Long_client_values_are_truncated_to_the_column_sizes()
    {
        AsPlatformAdmin();
        _client.Setup(c => c.UserAgent).Returns(new string('a', 2000));

        await Build<AuditedCommand, Result<string>>().Handle(
            new AuditedCommand(Guid.NewGuid(), "x"),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _platformRows.Rows.Single().UserAgent!.Length.Should().Be(512);
    }

    internal sealed class FakeUnitOfWork : IUnitOfWork
    {
        public bool InTransaction { get; private set; }
        public int TransactionCount { get; private set; }
        public int CommittedTransactions { get; private set; }
        public int SavesInsideTransaction { get; private set; }
        public int SavesOutsideTransaction { get; private set; }
        public bool ClearedBeforeFailureSave { get; private set; }
        private bool _cleared;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (InTransaction)
                SavesInsideTransaction++;
            else
            {
                SavesOutsideTransaction++;
                ClearedBeforeFailureSave = _cleared;
            }

            return Task.FromResult(1);
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            TransactionCount++;
            InTransaction = true;
            try
            {
                var result = await operation(cancellationToken);
                CommittedTransactions++;
                return result;
            }
            finally
            {
                InTransaction = false;
            }
        }

        public void ClearTracking() => _cleared = true;
    }

    internal sealed class FakePlatformAuditLogs : IPlatformAuditLogRepository
    {
        public List<PlatformAuditLog> Rows { get; } = new();
        public bool ThrowOnAdd { get; set; }

        public Task AddAsync(PlatformAuditLog entry, CancellationToken ct = default)
        {
            if (ThrowOnAdd)
            {
                ThrowOnAdd = false; // only the in-transaction write fails
                throw new InvalidOperationException("audit table unavailable");
            }

            Rows.Add(entry);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<PlatformAuditLog> Items, int Total)> ListAsync(
            PlatformAuditLogFilter filter, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<PlatformAuditLog>, int)>((Rows, Rows.Count));
    }

    internal sealed class FakeTenantAuditLogs : IAuditLogRepository
    {
        public List<AuditLog> Rows { get; } = new();

        public Task AddAsync(AuditLog auditLog, CancellationToken ct = default)
        {
            Rows.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<AuditLog> Items, int Total)> ListByTenantIdAsync(
            Guid tenantId, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<AuditLog>, int)>((Rows, Rows.Count));
    }
}
