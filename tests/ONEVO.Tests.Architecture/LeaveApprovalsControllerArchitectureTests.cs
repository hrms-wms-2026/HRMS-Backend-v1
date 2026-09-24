using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Contracts.Leave.Approvals;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeaveApprovalsControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LeaveApprovalsController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("TenantPolicy", attr!.Policy);
    }

    [Fact]
    public void Actions_UseExpectedPermissions()
    {
        Assert.Equal("leave:approve", GetPermission(nameof(LeaveApprovalsController.PendingApprovals)));
        Assert.Equal("leave:read", GetPermission(nameof(LeaveApprovalsController.All)));
        Assert.Equal("leave:approve", GetPermission(nameof(LeaveApprovalsController.ApprovalDetail)));
        Assert.Equal("leave:approve", GetPermission(nameof(LeaveApprovalsController.Approve)));
        Assert.Equal("leave:approve", GetPermission(nameof(LeaveApprovalsController.Reject)));
        Assert.Equal("leave:approve", GetPermission(nameof(LeaveApprovalsController.RequestInfo)));
        Assert.Equal("leave:read-own", GetPermission(nameof(LeaveApprovalsController.RespondInfo)));
        Assert.Equal("leave:approve", GetPermission(nameof(LeaveApprovalsController.BulkApprove)));
        Assert.Equal("leave:approve", GetPermission(nameof(LeaveApprovalsController.BulkReject)));
    }

    [Fact]
    public void Controller_InjectsIMediatorOnly()
    {
        var constructor = Assert.Single(ControllerType.GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal("IMediator", parameter.ParameterType.Name);
    }

    [Fact]
    public void RequestContracts_DoNotExposeTenantId()
    {
        foreach (var contractType in new[]
        {
            typeof(ApproveLeaveRequestRequest),
            typeof(RejectLeaveRequestRequest),
            typeof(RequestLeaveInformationRequest),
            typeof(RespondLeaveInformationRequest),
            typeof(BulkApproveLeaveRequestsRequest),
            typeof(BulkRejectLeaveRequestsRequest)
        })
        {
            var names = contractType.GetProperties().Select(p => p.Name);
            Assert.DoesNotContain(names, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string GetPermission(string methodName)
    {
        var method = ControllerType.GetMethod(methodName);
        var attribute = method!.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);
        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }
}
