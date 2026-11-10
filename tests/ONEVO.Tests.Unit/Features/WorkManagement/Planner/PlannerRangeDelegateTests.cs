using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Commands;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Spec "Seeing the principal's calendar": GET /planner/me/range?forEmployeeId= reads the principal's days for an
/// active delegate; anyone else gets 403.</summary>
public sealed class PlannerRangeDelegateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 11, 10);
    private static readonly DateOnly To = new(2026, 11, 16);

    private readonly Guid _secretary = Guid.NewGuid();
    private readonly Guid _secretaryUser = Guid.NewGuid();
    private readonly Guid _ceo = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarTimezoneResolver> _timezones = new();
    private readonly Mock<IMeetingDelegateRepository> _delegates = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public PlannerRangeDelegateTests()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(Tenant);
        _user.SetupGet(u => u.UserId).Returns(_secretaryUser);
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(Tenant, _secretaryUser, It.IsAny<CancellationToken>())).ReturnsAsync(_secretary);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>());
    }

    private PlannerRequestHandler Sut()
    {
        var planner = new PlannerService(Mock.Of<IPlannerRepository>(), Mock.Of<IMeetingRepository>(), _timezones.Object, _employees.Object,
            Mock.Of<IUnitOfWork>(), _clock.Object, Options.Create(new PlannerOptions { Enabled = true }));
        return new PlannerRequestHandler(_user.Object, _identity.Object, planner, _delegates.Object, _clock.Object);
    }

    private void SecretaryIsDelegate(bool active)
        => _delegates.Setup(d => d.IsActiveDelegateAsync(Tenant, _ceo, _secretary, Now, It.IsAny<CancellationToken>())).ReturnsAsync(active);

    /// <summary>Every employee id the planner looked up while building the range.</summary>
    private IEnumerable<Guid> LookedUp() => _employees.Invocations.Concat(_timezones.Invocations)
        .SelectMany(i => i.Arguments)
        .SelectMany(a => a is IEnumerable<Guid> many ? many : a is Guid one ? [one] : [])
        .Where(id => id != Tenant);

    [Fact]
    public async Task ADelegate_ReadsThePrincipalsRange()
    {
        SecretaryIsDelegate(active: true);

        await Sut().Handle(new GetMyPlannerRangeQuery(From, To, _ceo), CancellationToken.None);

        Assert.Contains(_ceo, LookedUp());
        Assert.DoesNotContain(_secretary, LookedUp());
    }

    [Fact]
    public async Task ANonDelegate_Is403_AndReadsNothing()
    {
        SecretaryIsDelegate(active: false);

        var result = await Sut().Handle(new GetMyPlannerRangeQuery(From, To, _ceo), CancellationToken.None);

        Assert.Equal((403, MeetingErrorCodes.DelegateRequired), (result.StatusCode, result.ErrorCode));
        Assert.Empty(LookedUp());
    }

    [Fact]
    public async Task AnExpiredDelegate_Is403()
    {
        SecretaryIsDelegate(active: false);

        var result = await Sut().Handle(new GetMyPlannerRangeQuery(From, To, _ceo), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NoForEmployeeId_BehavesLikeToday()
    {
        await Sut().Handle(new GetMyPlannerRangeQuery(From, To), CancellationToken.None);

        Assert.Contains(_secretary, LookedUp());
        _delegates.VerifyNoOtherCalls();
    }
}
