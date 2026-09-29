using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public class ExceptionScopeResolverTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _actorEmployeeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _reportId = Guid.NewGuid();

    public ExceptionScopeResolverTests()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = _actorEmployeeId, TenantId = _tenantId, UserId = _userId, LegalEntityId = _legalEntityId });
        _authority.Setup(a => a.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(_userId, _legalEntityId, false, [_reportId, _actorEmployeeId]));
    }

    private void Grant(params string[] permissions) =>
        _currentUser.Setup(u => u.HasPermission(It.IsAny<string>()))
            .Returns((string p) => permissions.Contains(p));

    private ExceptionScopeResolver CreateSut() => new(_currentUser.Object, _employees.Object, _authority.Object);

    [Fact]
    public async Task NoManagerHrOrExceptionPermission_ReturnsNull()
    {
        Grant();

        var scope = await CreateSut().ResolveAsync(forAction: false, [_reportId], CancellationToken.None);

        scope.Should().BeNull();
    }

    [Fact]
    public async Task Manager_AlsoSeesCandidatesTheyAreTheExactApproverFor()
    {
        Grant(ExceptionPermissions.ManagerReview);
        var reportingLineReport = Guid.NewGuid();
        _authority.Setup(a => a.ResolveApprovalInboxScopeAsync(It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([reportingLineReport]);

        var scope = await CreateSut().ResolveAsync(forAction: false, [reportingLineReport, Guid.NewGuid()], CancellationToken.None);

        scope!.EmployeeIds.Should().BeEquivalentTo([_reportId, reportingLineReport]);
        _authority.Verify(a => a.ResolveApprovalInboxScopeAsync(
            It.Is<EmployeeApprovalInboxScopeRequest>(r =>
                r.RequiredPermission == ExceptionPermissions.ManagerReview
                && r.LegalEntityId == _legalEntityId
                && r.CandidateEmployeeIds.Contains(reportingLineReport)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Manager_SeesOnlyCoveredEmployees_FromAuthorityResolver_WithoutSelf()
    {
        Grant(ExceptionPermissions.ManagerReview);

        var scope = await CreateSut().ResolveAsync(forAction: true, [], CancellationToken.None);

        scope.Should().NotBeNull();
        scope!.IsHr.Should().BeFalse();
        scope.EmployeeIds.Should().BeEquivalentTo([_reportId]);
        scope.CanSee(_reportId).Should().BeTrue();
        scope.CanSee(_actorEmployeeId).Should().BeFalse();
        scope.CanSee(Guid.NewGuid()).Should().BeFalse();
        _authority.Verify(a => a.ResolveVisibilityAsync(
            It.Is<EmployeeAuthorityVisibilityRequest>(r =>
                r.RequiredPermission == ExceptionPermissions.ManagerReview
                && r.LegalEntityId == _legalEntityId
                && !r.IncludeSelf
                && r.Purpose == EmployeeAuthorityPurpose.ExceptionAlertReview),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Hr_SeesEveryone_ButNeverThemselves_WithoutAskingTheResolver()
    {
        Grant(ExceptionPermissions.HrReview);

        var scope = await CreateSut().ResolveAsync(forAction: true, [], CancellationToken.None);

        scope!.IsHr.Should().BeTrue();
        scope.CanSee(Guid.NewGuid()).Should().BeTrue();
        scope.CanSee(_actorEmployeeId).Should().BeFalse();
        _authority.Verify(a => a.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExceptionsViewOnly_CanList_ButCannotAct()
    {
        Grant(ExceptionPermissions.View);

        (await CreateSut().ResolveAsync(forAction: false, [], CancellationToken.None)).Should().NotBeNull();
        (await CreateSut().ResolveAsync(forAction: true, [], CancellationToken.None)).Should().BeNull();
    }
}
