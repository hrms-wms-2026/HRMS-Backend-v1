using System.Text.Json;
using ONEVO.Application.Common.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Documents;

/// <summary>Categories an employee can file a personal document under (Profile → My documents).</summary>
public static class EmployeeDocumentTypes
{
    public const string Cv = "cv";
    public const string Identity = "identity";
    public const string Certificate = "certificate";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = new[] { Cv, Identity, Certificate, Other };

    public static bool IsValid(string? type) => type is not null && All.Contains(type);
}

/// <summary>Shape stored in entity_assets.metadata_json for an employee document.</summary>
public sealed record EmployeeDocumentMetadata(string DocumentType, string Name)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static EmployeeDocumentMetadata? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<EmployeeDocumentMetadata>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record EmployeeDocumentResponse(
    Guid Id,
    Guid FileId,
    string Name,
    string DocumentType,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset UploadedAt)
{
    public static EmployeeDocumentResponse From(EntityAssetWithFileAndMetadata asset)
    {
        var metadata = EmployeeDocumentMetadata.TryParse(asset.MetadataJson);
        var name = string.IsNullOrWhiteSpace(metadata?.Name)
            ? Path.GetFileNameWithoutExtension(asset.OriginalFileName)
            : metadata!.Name;
        var type = EmployeeDocumentTypes.IsValid(metadata?.DocumentType) ? metadata!.DocumentType : EmployeeDocumentTypes.Other;
        return new EmployeeDocumentResponse(
            asset.Id, asset.FileRecordId, name, type, asset.OriginalFileName, asset.ContentType, asset.FileSizeBytes, asset.CreatedAt);
    }
}
