using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.BalanceAudit.Queries.ListBalanceAudit;
using ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.BalanceAudit;

public class ListBalanceAuditQueryHandlerTests
{
    private readonly Mock<ILeaveBalanceAuditRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListBalanceAuditQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_ReturnsRowsFromRepository()
    {
        var row = new LeaveBalanceAuditRow(
            new LeaveBalanceAudit
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = Guid.NewGuid(), LeaveTypeId = Guid.NewGuid(),
                ChangeType = LeaveBalanceChangeTypes.Deduction, DaysChanged = -3m, BalanceAfter = 7m,
                Reason = "Leave approved", CreatedAt = DateTimeOffset.UtcNow
            },
            "EMP001", "Priya Kumar", "Annual Leave", "ANNUAL");

        _repoMock.Setup(r => r.ListRowsAsync(_tenantId, It.IsAny<LeaveBalanceAuditListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([row]);

        var handler = new ListBalanceAuditQueryHandler(_repoMock.Object, _currentUserMock.Object);
        var result = await handler.Handle(
            new ListBalanceAuditQuery(EmployeeId: null, LeaveTypeId: null, ChangeType: null, FromDate: null, ToDate: null, Page: 1, PageSize: 25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Priya Kumar", result.Value![0].EmployeeName);
        Assert.Equal(-3m, result.Value[0].DaysChanged);
    }
}
