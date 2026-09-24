namespace ONEVO.Application.Features.Monitoring.AppUsage.DTOs.Responses;

public record AppUsageSnapshotDto
{
    public Guid Id { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public string? ProcessName { get; init; }
    public string? WindowTitleHash { get; init; }
}
