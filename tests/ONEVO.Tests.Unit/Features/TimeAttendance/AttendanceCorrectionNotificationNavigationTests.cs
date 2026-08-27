using FluentAssertions;
using ONEVO.Api.Contracts.SharedPlatform.Notifications;
using ONEVO.Application.Features.SharedPlatform.Notifications.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceCorrectionNotificationNavigationTests
{
    private static readonly Guid CorrectionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ApprovalRequest_ResolvesToAttendanceCorrectionApprovalDestination()
    {
        var viewModel = Notification("attendance_correction_request_created").ToViewModel();

        viewModel.Destination.Should().NotBeNull();
        viewModel.Destination!.NotificationType.Should().Be("attendance_correction_request_created");
        viewModel.Destination.AttendanceCorrectionId.Should().Be(CorrectionId);
        viewModel.Destination.WorkAreaChangeRequestId.Should().BeNull();
        viewModel.Destination.DestinationKey.Should().Be("attendance_correction_approval");
        viewModel.Destination.IsNavigable.Should().BeTrue();
        viewModel.Destination.LegalEntityId.Should().BeNull();
    }

    [Theory]
    [InlineData("attendance_correction_request_decided", "approved")]
    [InlineData("attendance_correction_request_decided", "rejected")]
    [InlineData("attendance_correction_request_cancelled", "cancelled")]
    public void RequesterStatusNotifications_AreExplicitlyNonNavigableUntilAFrontendDestinationExists(
        string templateCode, string outcome)
    {
        var viewModel = Notification(templateCode, body: $"Your attendance correction request was {outcome}.").ToViewModel();

        viewModel.Destination.Should().NotBeNull();
        viewModel.Destination!.AttendanceCorrectionId.Should().Be(CorrectionId);
        viewModel.Destination.WorkAreaChangeRequestId.Should().BeNull();
        viewModel.Destination.DestinationKey.Should().BeNull();
        viewModel.Destination.IsNavigable.Should().BeFalse();
    }

    [Fact]
    public void WorkAreaApprovalRequest_UsesWorkAreaIdAndApprovalDestination()
    {
        var viewModel = Notification(
            "work_area_change_request_created",
            relatedType: "work_area_change_request",
            relatedId: CorrectionId).ToViewModel();

        viewModel.Destination.Should().NotBeNull();
        viewModel.Destination!.WorkAreaChangeRequestId.Should().Be(CorrectionId);
        viewModel.Destination.AttendanceCorrectionId.Should().BeNull();
        viewModel.Destination.DestinationKey.Should().Be("work_area_change_approval");
        viewModel.Destination.IsNavigable.Should().BeTrue();
    }

    [Theory]
    [InlineData("work_area_change_request_decided")]
    [InlineData("work_area_change_request_cancelled")]
    public void WorkAreaDecisionNotifications_AreNonNavigableAndKeepWorkAreaId(string templateCode)
    {
        var viewModel = Notification(
            templateCode,
            relatedType: "work_area_change_request",
            relatedId: CorrectionId).ToViewModel();

        viewModel.Destination.Should().NotBeNull();
        viewModel.Destination!.WorkAreaChangeRequestId.Should().Be(CorrectionId);
        viewModel.Destination.AttendanceCorrectionId.Should().BeNull();
        viewModel.Destination.DestinationKey.Should().BeNull();
        viewModel.Destination.IsNavigable.Should().BeFalse();
    }

    [Fact]
    public void UnrelatedNotificationTypes_AreNotChanged()
    {
        var viewModel = Notification("work_task_creation_request_created", "task", Guid.NewGuid()).ToViewModel();

        viewModel.Destination.Should().BeNull();
    }

    private static NotificationResponse Notification(
        string templateCode, string relatedType = "attendance_correction", Guid? relatedId = null,
        string body = "Body")
        => new(Guid.NewGuid(), templateCode, "Title", body, relatedType,
            relatedId ?? CorrectionId, false, null, CreatedAt);
}
