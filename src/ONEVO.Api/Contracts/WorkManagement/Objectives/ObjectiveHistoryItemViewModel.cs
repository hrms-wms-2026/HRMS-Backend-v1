namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveHistoryItemViewModel(
    Guid ObjectiveId, string Title, Guid ProjectId, bool IsAchieved, DateTimeOffset? RemovedAt);
