namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

/// <summary>The payload is the frontend's form snapshot sent as a real JSON object (not a string).</summary>
public sealed record SaveTaskDraftRequest(Guid ProjectId, string? Title, System.Text.Json.JsonElement Payload);
