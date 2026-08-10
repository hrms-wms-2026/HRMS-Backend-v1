using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.GetOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.ListOnboardingDrafts;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class GetOnboardingDraftQueryHandlerTests
{
    private readonly Mock<IOnboardingDraftRepository> _draftRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public GetOnboardingDraftQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
    }

    private GetOnboardingDraftQueryHandler CreateHandler() => new(_draftRepository.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDraftDoesNotExist()
    {
        var draftId = Guid.NewGuid();
        _draftRepository
            .Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OnboardingDraftEntity?)null);

        var result = await CreateHandler().Handle(new GetOnboardingDraftQuery(draftId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenCallerDidNotStartTheDraftAndLacksEmployeesWrite()
    {
        var draftId = Guid.NewGuid();
        _draftRepository
            .Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingDraftEntity { Id = draftId, TenantId = _tenantId, StartedById = Guid.NewGuid() });
        _currentUser.Setup(u => u.HasPermission("employees:write")).Returns(false);

        var result = await CreateHandler().Handle(new GetOnboardingDraftQuery(draftId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsDraft_WhenCallerIsTheStarter()
    {
        var draftId = Guid.NewGuid();
        _draftRepository
            .Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingDraftEntity { Id = draftId, TenantId = _tenantId, StartedById = _userId });
        var response = new OnboardingDraftResponse(draftId, "Ada", "ada@test.dev", Guid.NewGuid(), null, null,
            "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null,
            "draft", "saved_manually", "employee_details", _userId, "1");
        _draftRepository
            .Setup(r => r.GetResponseByIdAsync(_tenantId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await CreateHandler().Handle(new GetOnboardingDraftQuery(draftId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Value);
    }
}

public sealed class ListOnboardingDraftsQueryHandlerTests
{
    private readonly Mock<IOnboardingDraftRepository> _draftRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ListOnboardingDraftsQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _draftRepository
            .Setup(r => r.ListWithNamesAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<DraftListItemResponse>(), 0));
    }

    private ListOnboardingDraftsQueryHandler CreateHandler() => new(_draftRepository.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_ScopesToCallersOwnDrafts_WhenLackingEmployeesWrite()
    {
        _currentUser.Setup(u => u.HasPermission("employees:write")).Returns(false);

        await CreateHandler().Handle(new ListOnboardingDraftsQuery(), CancellationToken.None);

        _draftRepository.Verify(r => r.ListWithNamesAsync(
            _tenantId, _userId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShowsAllTenantDrafts_WhenCallerHasEmployeesWrite()
    {
        _currentUser.Setup(u => u.HasPermission("employees:write")).Returns(true);

        await CreateHandler().Handle(new ListOnboardingDraftsQuery(), CancellationToken.None);

        _draftRepository.Verify(r => r.ListWithNamesAsync(
            _tenantId, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
