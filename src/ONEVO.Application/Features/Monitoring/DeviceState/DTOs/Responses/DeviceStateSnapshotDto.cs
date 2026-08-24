namespace ONEVO.Application.Features.Monitoring.DeviceState.DTOs.Responses;

public record DeviceStateSnapshotDto
{
    public Guid Id { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public int IdleSeconds { get; init; }
    public bool IsIdle { get; init; }
}
