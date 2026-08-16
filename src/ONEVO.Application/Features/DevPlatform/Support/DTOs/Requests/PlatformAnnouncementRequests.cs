using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Support.DTOs.Requests;

public sealed record CreatePlatformAnnouncementRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("audience")] string? Audience);
