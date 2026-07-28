using Moq;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetEmployeeDailySummary;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Tests.Unit.Features.ActivityMonitoring;

public sealed class GetEmployeeDailySummaryQueryHandlerTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 7, 27);

    private readonly Mock<IActivityMonitoringRepository> _repo = new();

    [Fact]
    public async Task Handle_SummaryExistsWithNotices_ReturnsDtoWithTimestampedNotices()
    {
        var summary = MakeSummary();
        var notices = new List<MonitoringConsentEvent>
        {
            new()
            {
                IncidentId = Guid.NewGuid(),
                OccurredAt = new DateTimeOffset(2026, 7, 27, 9, 30, 0, TimeSpan.Zero),
                Decision = "denied"
            },
            new()
            {
                IncidentId = Guid.NewGuid(),
                OccurredAt = new DateTimeOffset(2026, 7, 27, 11, 0, 0, TimeSpan.Zero),
                Decision = "allowed"
            }
        };

        _repo.Setup(r => r.GetDailySummaryAsync(EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);
        _repo.Setup(r => r.GetConsentNoticesAsync(EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notices);

        var result = await CreateHandler().Handle(
            new GetEmployeeDailySummaryQuery(EmployeeId, Date), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value;
        Assert.NotNull(dto);
        Assert.Equal(EmployeeId, dto.EmployeeId);
        Assert.Equal(Date, dto.Date);
        Assert.Equal(2, dto.ConsentNotices.Count);
        Assert.All(dto.ConsentNotices, n => Assert.NotEqual(Guid.Empty, n.IncidentId));

        // Employee view carries exact timestamps
        Assert.Equal(
            new DateTimeOffset(2026, 7, 27, 9, 30, 0, TimeSpan.Zero),
            dto.ConsentNotices[0].OccurredAt);
        Assert.Equal("denied", dto.ConsentNotices[0].Decision);
    }

    [Fact]
    public async Task Handle_SummaryExistsWithNoNotices_ReturnsDtoWithEmptyNotices()
    {
        _repo.Setup(r => r.GetDailySummaryAsync(EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSummary());
        _repo.Setup(r => r.GetConsentNoticesAsync(EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoringConsentEvent>());

        var result = await CreateHandler().Handle(
            new GetEmployeeDailySummaryQuery(EmployeeId, Date), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.ConsentNotices);
    }

    [Fact]
    public async Task Handle_NoSummaryForDay_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetDailySummaryAsync(EmployeeId, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActivityDailySummary?)null);

        var result = await CreateHandler().Handle(
            new GetEmployeeDailySummaryQuery(EmployeeId, Date), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        _repo.Verify(r => r.GetConsentNoticesAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private GetEmployeeDailySummaryQueryHandler CreateHandler() =>
        new(_repo.Object);

    private static ActivityDailySummary MakeSummary() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EmployeeId = EmployeeId,
        Date = Date,
        TotalActiveMinutes = 360,
        TotalIdleMinutes = 60,
        TotalMeetingMinutes = 30,
        ActivePercentage = 80m,
        ActivityScore = 75m
    };
}
