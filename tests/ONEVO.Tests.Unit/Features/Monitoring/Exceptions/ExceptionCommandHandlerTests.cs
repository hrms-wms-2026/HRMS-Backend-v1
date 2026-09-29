using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using DomainException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public class ExceptionCommandHandlerTests
{
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExceptionScopeResolver> _scope = new();
    private readonly Mock<IExceptionAlertRouter> _alerts = new();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero) };

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _reportId = Guid.NewGuid();
    private DomainException _case;

    public ExceptionCommandHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _case = NewCase(ExceptionStatus.Open, _reportId);
        _exceptions.Setup(e => e.GetByIdAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _case);
        _scope.Setup(s => s.ResolveAsync(true, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, Guid.NewGuid(), [_reportId]));
    }

    private DomainException NewCase(ExceptionStatus status, Guid employeeId) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = employeeId,
        Type = ExceptionType.SustainedLowActivity, Status = status,
        Title = "Sustained low activity", Description = "desc", DetectedAt = _clock.UtcNow.AddDays(-1)
    };

    private AcknowledgeExceptionCommandHandler Acknowledge() => new(_exceptions.Object, _currentUser.Object, _scope.Object, _clock);
    private ResolveExceptionCommandHandler Resolve() => new(_exceptions.Object, _currentUser.Object, _scope.Object, _clock);
    private EscalateExceptionCommandHandler Escalate() =>
        new(_exceptions.Object, _currentUser.Object, _scope.Object, _alerts.Object, _clock);

    [Fact]
    public async Task NoAccess_IsForbidden()
    {
        _scope.Setup(s => s.ResolveAsync(true, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);

        var result = await Resolve().Handle(new ResolveExceptionCommand(_case.Id), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task CaseForAnEmployeeOutsideTheManagersScope_IsNotFound_AndUntouched()
    {
        _case = NewCase(ExceptionStatus.Open, Guid.NewGuid());

        var ack = await Acknowledge().Handle(new AcknowledgeExceptionCommand(_case.Id), CancellationToken.None);
        var resolve = await Resolve().Handle(new ResolveExceptionCommand(_case.Id), CancellationToken.None);
        var escalate = await Escalate().Handle(new EscalateExceptionCommand(_case.Id), CancellationToken.None);

        ack.StatusCode.Should().Be(404);
        resolve.StatusCode.Should().Be(404);
        escalate.StatusCode.Should().Be(404);
        _case.Status.Should().Be(ExceptionStatus.Open);
        _exceptions.Verify(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Acknowledge_OpenCase_RecordsWhoAndWhen()
    {
        var result = await Acknowledge().Handle(new AcknowledgeExceptionCommand(_case.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _case.Status.Should().Be(ExceptionStatus.Acknowledged);
        _case.AcknowledgedById.Should().Be(_userId);
        _case.AcknowledgedAt.Should().Be(_clock.UtcNow);
    }

    [Theory]
    [InlineData(ExceptionStatus.Acknowledged)]
    [InlineData(ExceptionStatus.Resolved)]
    [InlineData(ExceptionStatus.Escalated)]
    public async Task Acknowledge_AlreadyAcknowledgedResolvedOrEscalated_IsConflict_AndStatusIsKept(ExceptionStatus status)
    {
        _case = NewCase(status, _reportId);

        var result = await Acknowledge().Handle(new AcknowledgeExceptionCommand(_case.Id), CancellationToken.None);

        result.StatusCode.Should().Be(409);
        _case.Status.Should().Be(status);
    }

    [Fact]
    public async Task Actions_AskTheScopeAboutTheCasesOwnEmployee()
    {
        await Resolve().Handle(new ResolveExceptionCommand(_case.Id), CancellationToken.None);

        _scope.Verify(s => s.ResolveAsync(
            true, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(_reportId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_KeepsTheTrimmedNote()
    {
        var result = await Resolve().Handle(new ResolveExceptionCommand(_case.Id, "  Talked to them, was on sick leave.  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _case.Status.Should().Be(ExceptionStatus.Resolved);
        _case.ResolvedById.Should().Be(_userId);
        _case.ResolutionNote.Should().Be("Talked to them, was on sick leave.");
    }

    [Fact]
    public async Task Resolve_AlreadyResolved_IsConflict()
    {
        _case = NewCase(ExceptionStatus.Resolved, _reportId);

        var result = await Resolve().Handle(new ResolveExceptionCommand(_case.Id), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Escalate_MovesCaseToHr_AndNotifiesHrBeforeSaving()
    {
        var order = new List<string>();
        _alerts.Setup(a => a.NotifyEscalatedAsync(It.IsAny<DomainException>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("notify")).Returns(Task.CompletedTask);
        _exceptions.Setup(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).ReturnsAsync(1);

        var result = await Escalate().Handle(new EscalateExceptionCommand(_case.Id, "Repeated, needs HR."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _case.Status.Should().Be(ExceptionStatus.Escalated);
        _case.EscalatedById.Should().Be(_userId);
        _case.EscalatedAt.Should().Be(_clock.UtcNow);
        _case.ResolutionNote.Should().Be("Repeated, needs HR.");
        order.Should().Equal("notify", "save");
    }

    [Theory]
    [InlineData(ExceptionStatus.Escalated)]
    [InlineData(ExceptionStatus.Resolved)]
    public async Task Escalate_AlreadyEscalatedOrResolved_IsConflict_AndNobodyIsAlerted(ExceptionStatus status)
    {
        _case = NewCase(status, _reportId);

        var result = await Escalate().Handle(new EscalateExceptionCommand(_case.Id), CancellationToken.None);

        result.StatusCode.Should().Be(409);
        _alerts.Verify(a => a.NotifyEscalatedAsync(It.IsAny<DomainException>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
