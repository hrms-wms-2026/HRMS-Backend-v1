using FluentAssertions;
using ONEVO.Application.Features.Auth.Permission;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class DerivedPermissionsTests
{
    [Theory]
    [InlineData("leave:approve")]
    [InlineData("leave:manage")]
    [InlineData("attendance:approve")]
    [InlineData("payroll:approve")]
    [InlineData("payroll:run")]
    [InlineData("performance:write")]
    [InlineData("performance:manage")]
    [InlineData("expense:approve")]
    [InlineData("tasks:approve")]
    [InlineData("documents:approve")]
    [InlineData("grievance:manage")]
    [InlineData("monitoring:alerts:read")]
    [InlineData("monitoring:alerts:resolve")]
    [InlineData("verification:review")]
    [InlineData("workflows:execute")]
    public void InboxTriggers_ContainsExpectedCode(string code)
        => DerivedPermissions.InboxTriggers.Contains(code).Should().BeTrue();

    [Theory]
    [InlineData("employees:read-own")]
    [InlineData("leave:read-own")]
    [InlineData("employees:read")]
    [InlineData("inbox:read")]
    public void InboxTriggers_DoesNotContainBasicOrSelfServiceCodes(string code)
        => DerivedPermissions.InboxTriggers.Contains(code).Should().BeFalse();

    [Theory]
    [InlineData("leave:approve")]
    [InlineData("leave:manage")]
    [InlineData("attendance:approve")]
    [InlineData("employees:write")]
    [InlineData("payroll:approve")]
    [InlineData("performance:manage")]
    [InlineData("monitoring:alerts:read")]
    [InlineData("tasks:approve")]
    public void NotificationTriggers_ContainsExpectedCode(string code)
        => DerivedPermissions.NotificationTriggers.Contains(code).Should().BeTrue();

    [Theory]
    [InlineData("employees:read-own")]
    [InlineData("notifications:read")]
    [InlineData("calendar:read")]
    public void NotificationTriggers_DoesNotContainBasicCodes(string code)
        => DerivedPermissions.NotificationTriggers.Contains(code).Should().BeFalse();
}
