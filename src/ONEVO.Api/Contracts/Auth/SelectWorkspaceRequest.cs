using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record SelectWorkspaceRequest(
    [property: JsonPropertyName("login_challenge")] string LoginChallenge,
    [property: JsonPropertyName("workspace")] string Workspace);
