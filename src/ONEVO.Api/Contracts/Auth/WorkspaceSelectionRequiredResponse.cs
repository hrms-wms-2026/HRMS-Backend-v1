using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record WorkspaceSelectionRequiredResponse(
    [property: JsonPropertyName("requires_workspace_selection")] bool RequiresWorkspaceSelection,
    [property: JsonPropertyName("login_challenge")] string LoginChallenge,
    [property: JsonPropertyName("workspaces")] IReadOnlyList<WorkspaceOptionResponse> Workspaces);
