using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Spec "The online link": adding a link always uses the organizer's calendar connection, so a delegate's link
/// comes from the principal's account.</summary>
public sealed class SeriesMeetingLinkServiceDelegateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly string Teams = CalendarEventMeetingProviders.MicrosoftTeams;

    private readonly Employee _ceo = new() { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Chief", LastName = "Executive" };
    private readonly Employee _secretary = new() { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Sara", LastName = "Fernando" };
    private readonly MeetingSeries _series;
    private readonly Mock<IMeetingRepository> _meetings = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IMeetingAccessResolver> _access = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public SeriesMeetingLinkServiceDelegateTests()
    {
        _series = new MeetingSeries { Id = Guid.NewGuid(), TenantId = Tenant, OrganizerEmployeeId = _ceo.Id, Title = "Board review" };
        _meetings.Setup(m => m.GetSeriesAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_series);
        _employees.Setup(e => e.GetByIdAsync(Tenant, _ceo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ceo);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<MeetingLinkResponse>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingLinkResponse>.Success(new MeetingLinkResponse(Guid.NewGuid(), Teams,
                CalendarEventMeetingStatuses.Provisioning, null, null, null, 1)));
    }

    private SeriesMeetingLinkService Sut() => new(Mock.Of<IMeetingLinkRepository>(), _meetings.Object, Mock.Of<IMeetingOccurrenceWriter>(),
        _connections.Object, Mock.Of<IMeetingProvisioner>(), Mock.Of<IMeetingNotifier>(), Mock.Of<IMeetingRecurrence>(), _employees.Object,
        Mock.Of<IOutboxWriter>(), _unitOfWork.Object, Mock.Of<IDateTimeProvider>(), _access.Object,
        Options.Create(new MeetingsOptions { Create = true }));

    private void CallerIsOrganizerRole(Guid callerEmployeeId, Guid? actingFor)
        => _access.Setup(a => a.ResolveForSeriesAsync(Tenant, callerEmployeeId, _series.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingAccess(MeetingRoles.Organizer, _series, null, actingFor));

    private void Connected(Guid userId)
        => _connections.Setup(c => c.GetByTenantUserProviderAsync(Tenant, userId, CalendarMeetingConnections.SourceFor(Teams), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), TenantId = Tenant, UserId = userId, Status = ExternalCalendarConnectionStatuses.Active,
                ScopesJson = $"[\"{CalendarMeetingConnections.RequiredScope(Teams)}\"]",
            });

    [Fact]
    public async Task ADelegateAddingALink_UsesThePrincipalsConnection()
    {
        CallerIsOrganizerRole(_secretary.Id, actingFor: _ceo.Id);
        Connected(_ceo.UserId);

        var result = await Sut().RegenerateAsync(Tenant, _secretary.Id, _secretary.UserId, _series.Id, new MeetingLinkRequest(Teams, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        _connections.Verify(c => c.GetByTenantUserProviderAsync(Tenant, _ceo.UserId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _connections.Verify(c => c.GetByTenantUserProviderAsync(Tenant, _secretary.UserId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ADelegateWithTheirOwnConnection_StillGets409_WhenThePrincipalHasNone()
    {
        CallerIsOrganizerRole(_secretary.Id, actingFor: _ceo.Id);
        Connected(_secretary.UserId);

        var result = await Sut().RegenerateAsync(Tenant, _secretary.Id, _secretary.UserId, _series.Id, new MeetingLinkRequest(Teams, false),
            CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.ProviderNotConnected), (result.StatusCode, result.ErrorCode));
        Assert.Contains("Chief Executive needs to connect their calendar account", result.Error);
    }

    [Fact]
    public async Task TheOrganizer_StillUsesTheirOwnConnection()
    {
        CallerIsOrganizerRole(_ceo.Id, actingFor: null);

        var result = await Sut().RegenerateAsync(Tenant, _ceo.Id, _ceo.UserId, _series.Id, new MeetingLinkRequest(Teams, false),
            CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.ProviderNotConnected), (result.StatusCode, result.ErrorCode));
        Assert.Equal("Connect your calendar account to add an online meeting.", result.Error);
        _connections.Verify(c => c.GetByTenantUserProviderAsync(Tenant, _ceo.UserId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
