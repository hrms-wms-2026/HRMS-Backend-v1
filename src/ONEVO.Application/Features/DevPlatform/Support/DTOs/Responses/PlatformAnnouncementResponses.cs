using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

public sealed record PlatformAnnouncementDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("audience")] string Audience,
    [property: JsonPropertyName("is_published")] bool IsPublished,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt);

public sealed record PlatformAnnouncementListResponseDto(
    [property: JsonPropertyName("items")] IReadOnlyList<PlatformAnnouncementDto> Items,
    [property: JsonPropertyName("total")] int TotalCount,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);
