using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using Xunit;
using DomainException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public class ExceptionAlertRouterTests
{
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();

    public ExceptionAlertRouterTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee
            {
                Id = _employeeId, TenantId = _tenantId, UserId = _employeeUserId,
                FirstName = "Kali", LastName = "Raj", LegalEntityId = _legalEntityId
            });
        // HR list includes the subject employee too - they must never be alerted about themselves.
        _permissions.Setup(p => p.ListUserIdsWithPermissionCodeAsync(
                _tenantId, ExceptionPermissions.HrReview, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_hrUserId, _employeeUserId]);
    }

    private ExceptionAlertRouter CreateSut() => new(
        _authority.Object, _employees.Object, _permissions.Object, _notifications.Object, _clock.Object);

    private DomainException Case() => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
        Type = ExceptionType.AttendanceIrregularity, Status = ExceptionStatus.Open,
        Title = "Attendance irregularity", Description = "Worked time dropped.", DetectedAt = DateTimeOffset.UtcNow
    };

    private void ManagerRoute(Guid approverUserId) =>
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                Guid.NewGuid(), approverUserId, Guid.NewGuid(), ExceptionPermissions.ManagerReview,
                EmployeeAuthorityPurpose.ExceptionAlertReview, EmployeeApprovalRouteSource.ReportingLine, null)));

    private void VerifySent(Guid userId, string template, Times times) =>
        _notifications.Verify(n => n.SendTemplatedAsync(
            _tenantId, userId, template, It.IsAny<IReadOnlyDictionary<string, string>>(),
            ExceptionPermissions.RelatedEntityType, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Detected_GoesToReportingManager_ResolvedThroughAuthorityResolver()
    {
        ManagerRoute(_managerUserId);

        await CreateSut().NotifyDetectedAsync(Case(), CancellationToken.None);

        _authority.Verify(a => a.ResolveApproverAsync(
            It.Is<EmployeeApprovalRouteRequest>(r =>
                r.SubjectEmployeeId == _employeeId
                && r.LegalEntityId == _legalEntityId
                && r.RequiredPermission == ExceptionPermissions.ManagerReview
                && r.Purpose == EmployeeAuthorityPurpose.ExceptionAlertReview),
            It.IsAny<CancellationToken>()), Times.Once);
        VerifySent(_managerUserId, ExceptionPermissions.DetectedTemplate, Times.Once());
        VerifySent(_hrUserId, ExceptionPermissions.DetectedTemplate, Times.Never());
    }

    [Fact]
    public async Task Detected_WithNoManager_FallsBackToHr_NeverTheEmployee()
    {
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.UnprocessableEntity("No eligible approver."));

        await CreateSut().NotifyDetectedAsync(Case(), CancellationToken.None);

        VerifySent(_hrUserId, ExceptionPermissions.DetectedTemplate, Times.Once());
        VerifySent(_employeeUserId, ExceptionPermissions.DetectedTemplate, Times.Never());
    }

    [Fact]
    public async Task Detected_WhenRouteResolvesToTheEmployeeThemselves_FallsBackToHr()
    {
        ManagerRoute(_employeeUserId);

        await CreateSut().NotifyDetectedAsync(Case(), CancellationToken.None);

        VerifySent(_employeeUserId, ExceptionPermissions.DetectedTemplate, Times.Never());
        VerifySent(_hrUserId, ExceptionPermissions.DetectedTemplate, Times.Once());
    }

    [Fact]
    public async Task Escalated_GoesToHr_WithEmployeeNameInPlaceholders()
    {
        ManagerRoute(_managerUserId);
        IReadOnlyDictionary<string, string>? sent = null;
        _notifications.Setup(n => n.SendTemplatedAsync(
                _tenantId, _hrUserId, ExceptionPermissions.EscalatedTemplate,
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, string _, IReadOnlyDictionary<string, string> p, string? _, Guid? _, CancellationToken _) => sent = p)
            .Returns(Task.CompletedTask);

        await CreateSut().NotifyEscalatedAsync(Case(), CancellationToken.None);

        VerifySent(_hrUserId, ExceptionPermissions.EscalatedTemplate, Times.Once());
        VerifySent(_managerUserId, ExceptionPermissions.EscalatedTemplate, Times.Never());
        VerifySent(_employeeUserId, ExceptionPermissions.EscalatedTemplate, Times.Never());
        sent!["employeeName"].Should().Be("Kali Raj");
    }
}
