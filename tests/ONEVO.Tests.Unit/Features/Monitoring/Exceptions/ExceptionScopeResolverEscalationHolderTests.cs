using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Tests.Unit.Features.Inbox;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

/// <summary>Final review: a calendar case escalated up the reporting line can be opened and worked by whoever holds it now,
/// even when management coverage does not show them the employee - and only that case.</summary>
public sealed class ExceptionScopeResolverEscalationHolderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private readonly Employee _skipLevel = new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), LegalEntityId = Guid.NewGuid() };
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly InMemoryInboxDecisionJournal _journal = new();

    public ExceptionScopeResolverEscalationHolderTests()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_skipLevel.UserId);
        _currentUser.Setup(u => u.HasPermission(ExceptionPermissions.ManagerReview)).Returns(true);
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, _skipLevel.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_skipLevel);
        _authority.Setup(a => a.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(_skipLevel.UserId, _skipLevel.LegalEntityId!.Value, false, []));
        _authority.Setup(a => a.ResolveApprovalInboxScopeAsync(It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private void Escalated(ItemRef item, int minutes, params Guid[] to)
        => _journal.Add(TenantId, item, InboxJournalActions.Escalate, Guid.NewGuid(), Now.AddMinutes(minutes),
            System.Text.Json.JsonSerializer.Serialize(new { escalatedTo = to }));

    [Fact]
    public async Task The_current_holder_of_a_calendar_case_gets_that_case_and_a_previous_holder_does_not()
    {
        var held = new ItemRef("calendar", "meeting_exception", Guid.NewGuid());
        var movedOn = new ItemRef("calendar", "meeting_exception", Guid.NewGuid());
        var monitoring = new ItemRef("monitoring", "exception_case", Guid.NewGuid());
        Escalated(held, 0, _skipLevel.Id);
        Escalated(movedOn, 0, _skipLevel.Id);
        Escalated(movedOn, 10, Guid.NewGuid()); // escalated further up since
        Escalated(monitoring, 0, _skipLevel.Id); // monitoring cases keep their own (coverage / HR) scope

        var scope = await new ExceptionScopeResolver(_currentUser.Object, _employees.Object, _authority.Object, _journal)
            .ResolveAsync(forAction: true, [Guid.NewGuid()], default);

        scope!.HeldCaseIds.Should().Equal(held.Id);
        scope.CanAct(held.Id, Guid.NewGuid()).Should().BeTrue();
        scope.CanAct(movedOn.Id, Guid.NewGuid()).Should().BeFalse();
        scope.CanAct(held.Id, _skipLevel.Id).Should().BeFalse("nobody works a case about themselves");
    }
}
