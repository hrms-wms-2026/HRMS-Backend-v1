using System.Reflection;
using FluentAssertions;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.ActivityMonitoring;

/// <summary>
/// Enforces the privacy boundary: manager view carries only aggregate counts,
/// employee self-view carries per-incident timestamped notices.
/// </summary>
public sealed class DailySummaryConsentPrivacyTests
{
    [Fact]
    public void ManagerSummaryDto_HasNoPerIncidentTimestamps()
    {
        var properties = typeof(ActivityDailySummaryDto).GetProperties();

        properties.Should().NotContain(
            p => p.PropertyType == typeof(DateTimeOffset) ||
                 p.PropertyType == typeof(DateTimeOffset?),
            "manager view must not leak per-incident capture timestamps");

        properties.Should().NotContain(
            p => p.PropertyType.IsGenericType &&
                 typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) &&
                 p.PropertyType != typeof(string),
            "manager view must not expose a per-incident notice collection");
    }

    [Fact]
    public void EmployeeSummaryDto_ExposesTimestampedNoticesCollection()
    {
        // These types do not exist yet — this test causes a compile error (RED).
        var noticeProps = typeof(EmployeeConsentNoticeDto).GetProperties();

        noticeProps.Should().Contain(
            p => p.Name == nameof(EmployeeConsentNoticeDto.OccurredAt) &&
                 p.PropertyType == typeof(DateTimeOffset),
            "each employee notice must carry the exact capture timestamp");

        noticeProps.Should().Contain(
            p => p.Name == nameof(EmployeeConsentNoticeDto.Decision) &&
                 p.PropertyType == typeof(string),
            "each employee notice must carry the consent decision");

        var summaryProps = typeof(EmployeeActivityDailySummaryDto).GetProperties();

        summaryProps.Should().Contain(
            p => p.Name == nameof(EmployeeActivityDailySummaryDto.ConsentNotices),
            "employee summary must expose a per-incident notices collection");
    }
}
