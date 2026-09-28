using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptions;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using Xunit;
using DomainException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public class GetExceptionsQueryHandlerTests
{
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExceptionScopeResolver> _scope = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Guid _actorEmployeeId = Guid.NewGuid();
    private readonly Guid _reportId = Guid.NewGuid();

    private GetExceptionsQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _exceptions.Setup(r => r.ListEmployeeIdsWithExceptionsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_reportId]);
        _employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee>
            {
                [_reportId] = new() { Id = _reportId, TenantId = TenantId, FirstName = "Thivan", LastName = "K" }
            });
        return new GetExceptionsQueryHandler(_exceptions.Object, _currentUser.Object, _scope.Object, _employees.Object);
    }

    private void SetupList(ExceptionListFilter expected)
    {
        _exceptions.Setup(r => r.GetListTotalCountAsync(TenantId, expected, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _exceptions.Setup(r => r.GetListAsync(TenantId, expected, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DomainException
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _reportId,
                Type = ExceptionType.SustainedLowActivity, Status = ExceptionStatus.Open,
                Title = "Sustained low activity", Description = "desc", DetectedAt = DateTimeOffset.UtcNow
            }]);
    }

    [Fact]
    public async Task Manager_ListIsLimitedToTheirScope_AndCarriesEmployeeNames()
    {
        IReadOnlyCollection<Guid> covered = [_reportId];
        _scope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, _actorEmployeeId, covered));
        SetupList(new ExceptionListFilter(ExceptionStatus.Open, null, covered, _actorEmployeeId));
        var sut = BuildSut();

        var result = await sut.Handle(new GetExceptionsQuery { Status = ExceptionStatus.Open }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(e => e.Title == "Sustained low activity" && e.EmployeeName == "Thivan K");
    }

    [Fact]
    public async Task Hr_ListIsTenantWide_ButExcludesTheirOwnCases()
    {
        _scope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(true, _actorEmployeeId, Array.Empty<Guid>()));
        SetupList(new ExceptionListFilter(null, null, null, _actorEmployeeId));
        var sut = BuildSut();

        var result = await sut.Handle(new GetExceptionsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task NoAccess_IsForbidden()
    {
        _scope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync((ExceptionScope?)null);
        var sut = BuildSut();

        var result = await sut.Handle(new GetExceptionsQuery(), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        _exceptions.Verify(r => r.GetListAsync(It.IsAny<Guid>(), It.IsAny<ExceptionListFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
