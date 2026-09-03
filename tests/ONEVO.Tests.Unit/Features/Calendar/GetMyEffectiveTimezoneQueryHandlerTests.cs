using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetMyEffectiveTimezone;
using ONEVO.Application.Features.Calendar.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetMyEffectiveTimezoneQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarTimezoneResolver> _resolver = new();

    private GetMyEffectiveTimezoneQueryHandler BuildSut() => new(_currentUser.Object, _resolver.Object);

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var result = await BuildSut().Handle(new GetMyEffectiveTimezoneQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Authenticated_ReturnsResolvedTimezone()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _resolver.Setup(x => x.ResolveForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync("Asia/Colombo");

        var result = await BuildSut().Handle(new GetMyEffectiveTimezoneQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Asia/Colombo", result.Value!.Timezone);
    }
}
