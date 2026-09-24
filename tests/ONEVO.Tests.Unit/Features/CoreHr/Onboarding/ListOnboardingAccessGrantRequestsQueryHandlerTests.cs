using System.Reflection;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListOnboardingAccessGrantRequests;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class ListOnboardingAccessGrantRequestsQueryHandlerTests
{
    private readonly Mock<IAccessGrantRequestRepository> _repository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListOnboardingAccessGrantRequestsQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
    }

    private ListOnboardingAccessGrantRequestsQueryHandler CreateHandler() => new(_repository.Object, _currentUser.Object);

    private static (IReadOnlyList<OnboardingAccessGrantRequestListItemResponse> Items, int TotalCount) EmptyResult()
        => (new List<OnboardingAccessGrantRequestListItemResponse>(), 0);

    [Fact]
    public async Task Handle_Defaults_QueriesPendingOnboardingRequestsForCurrentTenant()
    {
        _repository
            .Setup(r => r.ListOnboardingRequestsAsync(
                _tenantId, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyResult());

        var result = await CreateHandler().Handle(new ListOnboardingAccessGrantRequestsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repository.Verify(r => r.ListOnboardingRequestsAsync(
            _tenantId, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("pending", "Pending")]
    [InlineData("Approved", "Approved")]
    [InlineData("REJECTED", "Rejected")]
    [InlineData("cancelled", "Cancelled")]
    public async Task Handle_NormalizesStatusCaseInsensitively(string input, string stored)
    {
        _repository
            .Setup(r => r.ListOnboardingRequestsAsync(
                _tenantId, stored, AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyResult());

        var result = await CreateHandler().Handle(new ListOnboardingAccessGrantRequestsQuery(Status: input), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repository.Verify(r => r.ListOnboardingRequestsAsync(
            _tenantId, stored, AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnrecognizedStatus_ReturnsValidationFailure()
    {
        var result = await CreateHandler().Handle(new ListOnboardingAccessGrantRequestsQuery(Status: "bogus"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        _repository.Verify(r => r.ListOnboardingRequestsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnrecognizedActionType_ReturnsValidationFailure()
    {
        var result = await CreateHandler().Handle(new ListOnboardingAccessGrantRequestsQuery(ActionType: "transfer"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        _repository.Verify(r => r.ListOnboardingRequestsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task Handle_ClampsPageToAtLeastOne(int requestedPage, int expectedPage)
    {
        _repository
            .Setup(r => r.ListOnboardingRequestsAsync(
                _tenantId, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, expectedPage, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyResult());

        var result = await CreateHandler().Handle(new ListOnboardingAccessGrantRequestsQuery(Page: requestedPage), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPage, result.Value!.Page);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 100)]
    public async Task Handle_ClampsPageSizeBetweenOneAndOneHundred(int requestedPageSize, int expectedPageSize)
    {
        _repository
            .Setup(r => r.ListOnboardingRequestsAsync(
                _tenantId, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, expectedPageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyResult());

        var result = await CreateHandler().Handle(new ListOnboardingAccessGrantRequestsQuery(PageSize: requestedPageSize), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPageSize, result.Value!.PageSize);
    }

    [Fact]
    public async Task Handle_PassesSearchAndLegalEntityAndRoleFiltersThrough()
    {
        var legalEntityId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _repository
            .Setup(r => r.ListOnboardingRequestsAsync(
                _tenantId, "Pending", AccessGrantActionType.EmployeeOnboarding, legalEntityId, roleId, "jane", 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyResult());

        var result = await CreateHandler().Handle(
            new ListOnboardingAccessGrantRequestsQuery(Search: "jane", LegalEntityId: legalEntityId, RequestedRoleId: roleId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repository.Verify(r => r.ListOnboardingRequestsAsync(
            _tenantId, "Pending", AccessGrantActionType.EmployeeOnboarding, legalEntityId, roleId, "jane", 1, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Security: the query never carries a tenant identifier from the caller - tenant scoping
    // comes only from ICurrentUser.TenantId, resolved server-side.
    [Fact]
    public void Query_HasNoTenantIdProperty()
    {
        var properties = typeof(ListOnboardingAccessGrantRequestsQuery).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.DoesNotContain(properties, p => p.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase));
    }

    // Security: the list-item DTO must never carry raw tokens, hashes, or other security-sensitive
    // fields - only what an approver needs to review and decide.
    [Fact]
    public void ListItemResponse_HasNoSensitiveSecurityFields()
    {
        var forbidden = new[] { "token", "hash", "secret", "password", "credential" };
        var properties = typeof(OnboardingAccessGrantRequestListItemResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            Assert.DoesNotContain(forbidden, keyword => property.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }
}
