using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.WorkAreaChangeRequests;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Api.Filters;
using ONEVO.Application;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Architecture;

public sealed class WorkAreaChangeRequestsArchitectureTests
{
    private static readonly Type ControllerType = typeof(WorkAreaChangeRequestsController);

    [Fact]
    public void Controller_IsTenantScopedAndUsesExactBaseRoute()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.Attendance", ControllerType.Namespace);
        Assert.Equal("TenantPolicy", ControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("api/v1/attendance/work-area-change-requests", ControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void ApprovalEndpoints_RequireAttendanceApproval()
    {
        AssertPermission(nameof(WorkAreaChangeRequestsController.Approvals));
        AssertPermission(nameof(WorkAreaChangeRequestsController.Approve));
        AssertPermission(nameof(WorkAreaChangeRequestsController.Reject));
    }

    [Fact]
    public void SelfServiceEndpoints_DoNotRequireApprovalPermission()
    {
        foreach (var name in new[]
        {
            nameof(WorkAreaChangeRequestsController.Preview),
            nameof(WorkAreaChangeRequestsController.Create),
            nameof(WorkAreaChangeRequestsController.My),
            nameof(WorkAreaChangeRequestsController.Cancel)
        })
        {
            Assert.Null(ControllerType.GetMethod(name)!.GetCustomAttribute<RequirePermissionAttribute>());
        }
    }

    [Fact]
    public void RequestContracts_DoNotAcceptServerOwnedIdentifiers()
    {
        var forbidden = new[]
        {
            "TenantId", "EmployeeId", "LegalEntityId", "ApproverId", "ReviewedById",
            "Status", "ShiftAssignmentId", "AttachmentId"
        };

        Assert.DoesNotContain(typeof(WorkAreaChangeRequestRequest).GetProperties(),
            property => forbidden.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(ReviewWorkAreaChangeRequestRequest).GetProperties(),
            property => forbidden.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkAreaEntity_IsTenantOwnedAndLivesUnderTimeAttendance()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(WorkAreaChangeRequest)));
        Assert.Equal("ONEVO.Domain.Features.TimeAttendance.Entities", typeof(WorkAreaChangeRequest).Namespace);
    }

    [Fact]
    public void ApplicationAssembly_DoesNotReferenceEfCore()
    {
        var references = typeof(DependencyInjection).Assembly
            .GetReferencedAssemblies().Select(x => x.Name).ToList();
        Assert.DoesNotContain(references, x => x is "Microsoft.EntityFrameworkCore" or "Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Fact]
    public void Workflow_UsesProviderAuthorityAndDoesNotInsertNotificationRowsDirectly()
    {
        var path = SourcePath("src", "ONEVO.Application", "Features", "TimeAttendance", "Commands",
            "WorkAreaChangeRequests", "WorkAreaChangeRequestWorkflow.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("IDateTimeProvider", source);
        Assert.Contains("IEmployeeAuthorityResolver", source);
        Assert.Contains("EmployeeAuthorityPurpose.WorkAreaChangeApproval", source);
        Assert.Contains("PermissionCatalog.Attendance.Approve", source);
        Assert.DoesNotContain("DateTime.UtcNow", source);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", source);
        Assert.DoesNotContain("AddNotification", source);
        Assert.DoesNotContain("db.Notifications", source);
    }

    [Fact]
    public void NotificationMapping_DoesNotUseFalseAttendanceCorrectionIdentifierForWorkArea()
    {
        var path = SourcePath("src", "ONEVO.Api", "Contracts", "SharedPlatform", "Notifications",
            "NotificationContracts.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("WorkAreaChangeRequestId", source);
        Assert.Contains("AttendanceCorrectionId = isCorrection ? relatedId : null", source);
        Assert.DoesNotContain("isWorkArea ? relatedId : Guid.Empty", source);
    }

    private static void AssertPermission(string methodName)
    {
        var attribute = ControllerType.GetMethod(methodName)!.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);
        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("attendance:approve", field!.GetValue(attribute));
    }

    private static string SourcePath(params string[] parts)
        => Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
