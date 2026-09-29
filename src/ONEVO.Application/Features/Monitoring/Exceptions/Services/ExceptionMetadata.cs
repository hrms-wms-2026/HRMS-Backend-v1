using System.Text.Json;
using ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

/// <summary>
/// What an exception case carries in MetadataJson, captured when the case is raised - the only
/// moment the triggering attempt, scan, photo and device are all known. The evidence query
/// reads it back. Every field is optional so older cases (plain "{}") still read.
/// </summary>
public sealed record ExceptionMetadata
{
    public const string SourceFaceCheckOverride = "face_check_override";
    public const string SourceCheckInScan = "check_in_scan";

    /// <summary>Where an identity case came from: <see cref="SourceFaceCheckOverride"/> or <see cref="SourceCheckInScan"/>.</summary>
    public string? Source { get; init; }

    /// <summary>The day the case is about. The nightly job sets its target date; identity cases
    /// leave it null and the evidence query derives it from <see cref="OccurredAt"/> in the
    /// company timezone.</summary>
    public DateOnly? WorkDate { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }
    public Guid? FaceAttemptId { get; init; }
    public Guid? FaceScanId { get; init; }
    public Guid? CheckInId { get; init; }

    /// <summary>"clock_in" or "clock_out" for a face-check override.</summary>
    public string? Purpose { get; init; }

    public float? SimilarityScore { get; init; }
    public IReadOnlyList<string>? Reasons { get; init; }
    public Guid? DeviceRegistrationId { get; init; }

    /// <summary>file_records.Id of the photo that raised the case. Only ever served to reviewers
    /// who can see the case, as a short-lived signed URL from the evidence query - never in the list.</summary>
    public Guid? PhotoFileId { get; init; }

    /// <summary>The figures a nightly rule compared, captured when the case was raised.</summary>
    public IReadOnlyList<PatternMeasureDto>? Measures { get; init; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ExceptionMetadata Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ExceptionMetadata();
        try
        {
            return JsonSerializer.Deserialize<ExceptionMetadata>(json, Json) ?? new ExceptionMetadata();
        }
        catch (JsonException)
        {
            return new ExceptionMetadata();
        }
    }
}
