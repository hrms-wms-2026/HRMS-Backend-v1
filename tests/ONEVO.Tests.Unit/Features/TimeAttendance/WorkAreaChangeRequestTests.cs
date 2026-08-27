using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class WorkAreaChangeRequestTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 8, 25);

    private static LegalEntity DefaultLegalEntity() => new()
    {
        Id = LegalEntityId,
        Timezone = "UTC",
        WorkStartTime = new TimeOnly(9, 0),
        WorkEndTime = new TimeOnly(17, 0),
        StandardWorkingDays = "[1,2,3,4,5]"
    };

    private static Employee DefaultEmployee(int workModeId = 77) => new()
    {
        Id = EmployeeId, TenantId = TenantId, WorkModeId = workModeId
    };

    private static (Mock<IWorkModeRepository> WorkModes, Mock<IWorkAreaChangeRequestRepository> Requests, ExpectedWorkAreaResolver Resolver)
        BuildResolver(string workModeCode = "remote", WorkAreaChangeRequest? approved = null)
    {
        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkMode { Id = 77, Code = workModeCode, Label = workModeCode, IsActive = true }]);
        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(approved);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero));
        var resolver = new ExpectedWorkAreaResolver(clock.Object, workModes.Object, requests.Object);
        return (workModes, requests, resolver);
    }

    private static WorkAreaChangeRequest ApprovedRequest(string requestedWorkArea) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LegalEntityId = LegalEntityId,
        Date = Date, CurrentExpectedWorkArea = "onsite", RequestedWorkArea = requestedWorkArea,
        Reason = "Reason", Status = WorkAreaChangeRequest.StatusApproved
    };

    [Theory]
    [InlineData("onsite", "onsite")]
    [InlineData("on_site", "onsite")]
    [InlineData("remote", "remote")]
    [InlineData("hybrid", "either")]
    [InlineData("field", "field")]
    public async Task ExpectedAreaResolver_NoApprovedRequest_UsesActiveWorkModeCode(string code, string expected)
    {
        var (_, _, resolver) = BuildResolver(code);

        var result = await resolver.ResolveAsync(DefaultEmployee(), DefaultLegalEntity(), Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be(expected);
        result.Value.Source.Should().Be("active_employee_work_mode");
    }

    [Fact]
    public async Task ExpectedAreaResolver_FailsWhenWorkModeIsMissingOrInactive()
    {
        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkMode>());
        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        requests.Setup(x => x.GetApprovedForDateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        var resolver = new ExpectedWorkAreaResolver(clock.Object, workModes.Object, requests.Object);

        var result = await resolver.ResolveAsync(
            new Employee { Id = Guid.NewGuid(), WorkModeId = 999 },
            new LegalEntity { Timezone = "UTC" },
            new DateOnly(2026, 8, 25));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ExpectedAreaResolver_ApprovedRemoteOverride_OverridesPermanentOnsiteWorkMode()
    {
        var (_, _, resolver) = BuildResolver("onsite", ApprovedRequest("remote"));

        var result = await resolver.ResolveAsync(DefaultEmployee(), DefaultLegalEntity(), Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be("remote");
        result.Value.Source.Should().Be("approved_work_area_change_request");
    }

    [Fact]
    public async Task ExpectedAreaResolver_ApprovedOnsiteOverride_OverridesPermanentRemoteWorkMode()
    {
        var (_, _, resolver) = BuildResolver("remote", ApprovedRequest("onsite"));

        var result = await resolver.ResolveAsync(DefaultEmployee(), DefaultLegalEntity(), Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be("onsite");
        result.Value.Source.Should().Be("approved_work_area_change_request");
    }

    [Fact]
    public async Task ExpectedAreaResolver_HybridWithNoOverride_ResolvesToEitherFromActiveWorkMode()
    {
        var (_, _, resolver) = BuildResolver("hybrid", approved: null);

        var result = await resolver.ResolveAsync(DefaultEmployee(), DefaultLegalEntity(), Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be("either");
        result.Value.Source.Should().Be("active_employee_work_mode");
    }

    [Fact]
    public async Task ExpectedAreaResolver_NoApprovedRowReturnedByRepository_FallsBackToPermanentWorkMode()
    {
        // Filtering out pending/rejected/cancelled statuses happens in the repository query
        // (see EfWorkAreaChangeRequestRepositoryTests); this proves the resolver correctly falls
        // back to the permanent work mode whenever the repository reports no approved row.
        var (_, _, resolver) = BuildResolver("onsite", approved: null);

        var result = await resolver.ResolveAsync(DefaultEmployee(), DefaultLegalEntity(), Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be("onsite");
        result.Value.Source.Should().Be("active_employee_work_mode");
    }

    [Fact]
    public async Task ExpectedAreaResolver_AnotherDate_DoesNotOverride()
    {
        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkMode { Id = 77, Code = "onsite", Label = "onsite", IsActive = true }]);
        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        // Only the exact requested date resolves an approved row; any other date returns null.
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApprovedRequest("remote"));
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, Date.AddDays(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero));
        var resolver = new ExpectedWorkAreaResolver(clock.Object, workModes.Object, requests.Object);

        var result = await resolver.ResolveAsync(DefaultEmployee(), DefaultLegalEntity(), Date.AddDays(1));

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be("onsite");
        result.Value.Source.Should().Be("active_employee_work_mode");
    }

    [Fact]
    public async Task ExpectedAreaResolver_AnotherEmployee_DoesNotOverride()
    {
        var otherEmployeeId = Guid.NewGuid();
        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkMode { Id = 77, Code = "onsite", Label = "onsite", IsActive = true }]);
        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApprovedRequest("remote"));
        // A different employee never resolves the first employee's approved row.
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, otherEmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero));
        var resolver = new ExpectedWorkAreaResolver(clock.Object, workModes.Object, requests.Object);

        var result = await resolver.ResolveAsync(
            new Employee { Id = otherEmployeeId, TenantId = TenantId, WorkModeId = 77 },
            DefaultLegalEntity(),
            Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be("onsite");
        result.Value.Source.Should().Be("active_employee_work_mode");
    }

    [Fact]
    public async Task ExpectedAreaResolver_AnotherLegalEntity_DoesNotOverride()
    {
        var otherLegalEntityId = Guid.NewGuid();
        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkMode { Id = 77, Code = "onsite", Label = "onsite", IsActive = true }]);
        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApprovedRequest("remote"));
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, otherLegalEntityId, EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero));
        var resolver = new ExpectedWorkAreaResolver(clock.Object, workModes.Object, requests.Object);

        var result = await resolver.ResolveAsync(
            DefaultEmployee(),
            new LegalEntity
            {
                Id = otherLegalEntityId, Timezone = "UTC",
                WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0),
                StandardWorkingDays = "[1,2,3,4,5]"
            },
            Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be("onsite");
        result.Value.Source.Should().Be("active_employee_work_mode");
    }

    [Fact]
    public async Task ExpectedAreaResolver_AnotherTenant_DoesNotOverride()
    {
        var otherTenantId = Guid.NewGuid();
        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkMode { Id = 77, Code = "onsite", Label = "onsite", IsActive = true }]);
        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApprovedRequest("remote"));
        requests.Setup(x => x.GetApprovedForDateAsync(
                otherTenantId, LegalEntityId, EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero));
        var resolver = new ExpectedWorkAreaResolver(clock.Object, workModes.Object, requests.Object);

        var result = await resolver.ResolveAsync(
            new Employee { Id = EmployeeId, TenantId = otherTenantId, WorkModeId = 77 },
            DefaultLegalEntity(),
            Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkArea.Should().Be("onsite");
        result.Value.Source.Should().Be("active_employee_work_mode");
    }

    [Fact]
    public async Task ExpectedAreaResolver_ApprovedRequestWithInvalidRequestedArea_FailsClosedInstead_OfFallingBackToWorkMode()
    {
        var (_, _, resolver) = BuildResolver("onsite", ApprovedRequest("field"));

        var result = await resolver.ResolveAsync(DefaultEmployee(), DefaultLegalEntity(), Date);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ExpectedAreaResolver_InconsistentDuplicateApprovedRows_FailsClosed()
    {
        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkMode { Id = 77, Code = "onsite", Label = "onsite", IsActive = true }]);
        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        requests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InconsistentWorkAreaChangeRequestStateException());
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero));
        var resolver = new ExpectedWorkAreaResolver(clock.Object, workModes.Object, requests.Object);

        var result = await resolver.ResolveAsync(DefaultEmployee(), DefaultLegalEntity(), Date);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ExpectedAreaResolver_PreservesLegalEntityTimezoneFromScheduleResolution()
    {
        var (_, _, resolver) = BuildResolver("remote", ApprovedRequest("onsite"));

        var result = await resolver.ResolveAsync(
            DefaultEmployee(),
            new LegalEntity
            {
                Id = LegalEntityId, Timezone = "Asia/Colombo",
                WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0),
                StandardWorkingDays = "[1,2,3,4,5]"
            },
            Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Timezone.Should().Be("Asia/Colombo");
    }

    [Theory]
    [InlineData("onsite", false)]
    [InlineData("remote", false)]
    [InlineData("hybrid", true)]
    [InlineData("either", true)]
    [InlineData("field", true)]
    [InlineData("unknown", true)]
    [InlineData("", true)]
    public void RequestValidator_AllowsOnlyOnsiteAndRemoteTargets(string target, bool invalid)
    {
        var validator = new CreateWorkAreaChangeRequestCommandValidator();
        var result = validator.TestValidate(new CreateWorkAreaChangeRequestCommand(
            new DateOnly(2026, 8, 26), target, "Appointment"));

        result.IsValid.Should().Be(!invalid);
    }

    [Fact]
    public void RejectValidator_RequiresNonBlankReviewComment()
    {
        var validator = new RejectWorkAreaChangeRequestCommandValidator();

        validator.TestValidate(new RejectWorkAreaChangeRequestCommand(Guid.NewGuid(), " "))
            .ShouldHaveValidationErrorFor(x => x.ReviewComment);
    }

    [Fact]
    public void RequestValidator_RequiresReason()
    {
        var validator = new CreateWorkAreaChangeRequestCommandValidator();

        validator.TestValidate(new CreateWorkAreaChangeRequestCommand(
                new DateOnly(2026, 8, 26), "remote", " "))
            .ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
