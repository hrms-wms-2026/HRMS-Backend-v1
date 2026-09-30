using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.CancelOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class CancelOnboardingDraftCommandHandlerTests
{
    private readonly Mock<IOnboardingDraftRepository> _drafts = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public CancelOnboardingDraftCommandHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    }

    private CancelOnboardingDraftCommandHandler Sut() => new(_drafts.Object, _currentUser.Object, _clock.Object);

    private OnboardingDraftEntity Seed(string status, Guid? startedBy = null)
    {
        var d = new OnboardingDraftEntity { Id = Guid.NewGuid(), TenantId = _tenantId, Status = status, StartedById = startedBy ?? _userId };
        _drafts.Setup(r => r.GetTrackedAsync(_tenantId, d.Id, It.IsAny<CancellationToken>())).ReturnsAsync(d);
        return d;
    }

    [Fact]
    public async Task OwnDraft_IsCancelled_AndSaved()
    {
        var d = Seed(OnboardingDraftStatus.Draft);
        var r = await Sut().Handle(new CancelOnboardingDraftCommand(d.Id), CancellationToken.None);
        Assert.True(r.IsSuccess);
        Assert.Equal(OnboardingDraftStatus.Cancelled, d.Status);
        _drafts.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SomeoneElsesDraft_WithoutWrite_IsForbidden()
    {
        var d = Seed(OnboardingDraftStatus.Draft, startedBy: Guid.NewGuid());
        _currentUser.Setup(u => u.HasPermission("employees:write")).Returns(false);
        Assert.Equal(403, (await Sut().Handle(new CancelOnboardingDraftCommand(d.Id), CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task FinalizedDraft_IsConflict()
    {
        var d = Seed(OnboardingDraftStatus.Finalized);
        var r = await Sut().Handle(new CancelOnboardingDraftCommand(d.Id), CancellationToken.None);
        Assert.Equal(409, r.StatusCode);
        Assert.Equal("This draft has already been finalized.", r.Error);
    }

    [Fact]
    public async Task AlreadyCancelled_IsIdempotentSuccess_WithoutSaving()
    {
        var d = Seed(OnboardingDraftStatus.Cancelled);
        Assert.True((await Sut().Handle(new CancelOnboardingDraftCommand(d.Id), CancellationToken.None)).IsSuccess);
        _drafts.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_IsNotFound()
        => Assert.Equal(404, (await Sut().Handle(new CancelOnboardingDraftCommand(Guid.NewGuid()), CancellationToken.None)).StatusCode);
}
