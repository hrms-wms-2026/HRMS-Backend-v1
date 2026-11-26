using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Commands;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>A signed-in user with no employee profile in the tenant (e.g. an admin-only account) gets a code the
/// planner can explain, not the generic planner_not_found.</summary>
public sealed class PlannerRequestHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static PlannerRequestHandler HandlerForUserWithoutEmployee()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        // The planner service is never reached when there is no caller employee.
        return new PlannerRequestHandler(currentUser.Object, identity.Object, planner: null!);
    }

    [Fact]
    public async Task Range_for_a_user_without_an_employee_profile_answers_no_employee_profile()
    {
        var result = await HandlerForUserWithoutEmployee()
            .Handle(new GetMyPlannerRangeQuery(new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 29)), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
        Assert.Equal(PlannerErrorCodes.NoEmployeeProfile, result.ErrorCode);
    }

    [Fact]
    public async Task Focus_for_a_user_without_an_employee_profile_answers_no_employee_profile()
    {
        var result = await HandlerForUserWithoutEmployee().Handle(new GetMyPlannerFocusQuery(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
        Assert.Equal(PlannerErrorCodes.NoEmployeeProfile, result.ErrorCode);
    }
}
