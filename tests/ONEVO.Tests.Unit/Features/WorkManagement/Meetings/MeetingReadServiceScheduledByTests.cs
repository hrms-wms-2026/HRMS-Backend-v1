using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Spec "Frontend": the occurrence shows "Scheduled by" to the organizer role only, and only when someone other
/// than the organizer created the meeting.</summary>
public sealed class MeetingReadServiceScheduledByTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly Employee _ceo = new() { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Chief", LastName = "Executive" };
    private readonly Employee _secretary = new() { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Sara", LastName = "Fernando" };
    private readonly Mock<IMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingAccessResolver> _access = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly MeetingSeries _series;
    private readonly MeetingDetails _details;

    public MeetingReadServiceScheduledByTests()
    {
        _series = new MeetingSeries
        {
            Id = Guid.NewGuid(), TenantId = Tenant, OrganizerEmployeeId = _ceo.Id, Title = "Board review", DtStart = Start,
            DurationMinutes = 30, Timezone = "UTC", CreatedById = _secretary.UserId,
        };
        _details = new MeetingDetails
        {
            TaskId = Guid.NewGuid(), TenantId = Tenant, SeriesId = _series.Id, OriginalStart = Start, StartAt = Start, EndAt = Start.AddMinutes(30),
        };
        _meetings.Setup(m => m.GetOccurrenceAsync(Tenant, _series.Id, Start, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingOccurrenceRow(_details, "DAPI-M1", "Board review", null, Guid.NewGuid(), Guid.NewGuid()));
        _meetings.Setup(m => m.ListSeriesParticipantsAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _meetings.Setup(m => m.ListSeriesGuestsAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _meetings.Setup(m => m.ListOverridesAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee>());
        _employees.Setup(e => e.GetByIdAsync(Tenant, _ceo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ceo);
        _employees.Setup(e => e.GetByUserIdAsync(Tenant, _secretary.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_secretary);
        _employees.Setup(e => e.GetByUserIdAsync(Tenant, _ceo.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_ceo);
        _clock.SetupGet(c => c.UtcNow).Returns(Start.AddDays(-1));
    }

    private MeetingReadService Sut() => new(_meetings.Object, _access.Object, Mock.Of<IProjectRepository>(), Mock.Of<IObjectiveRepository>(),
        _employees.Object, Mock.Of<IMeetingRecurrence>(), _clock.Object);

    private async Task<string?> ScheduledByAsync(string role, Guid caller)
    {
        _access.Setup(a => a.ResolveForOccurrenceAsync(Tenant, caller, _series.Id, Start, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingAccess(role, _series, _details));
        var result = await Sut().GetOccurrenceAtAsync(Tenant, caller, _series.Id, Start, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.ScheduledByName;
    }

    [Fact]
    public async Task ScheduledByName_ShowsTheDelegate_ToTheOrganizerRoleOnly()
    {
        Assert.Equal("Sara Fernando", await ScheduledByAsync(MeetingRoles.Organizer, _ceo.Id));
        Assert.Null(await ScheduledByAsync(MeetingRoles.Attendee, Guid.NewGuid()));
    }

    [Fact]
    public async Task ScheduledByName_IsNull_WhenTheOrganizerCreatedIt()
    {
        _series.CreatedById = _ceo.UserId;

        Assert.Null(await ScheduledByAsync(MeetingRoles.Organizer, _ceo.Id));
    }
}
