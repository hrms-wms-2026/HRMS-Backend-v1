using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record WorkspaceOptionResponse(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("displayName")] string DisplayName);
