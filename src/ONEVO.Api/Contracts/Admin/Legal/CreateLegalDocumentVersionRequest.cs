using System.Text.Json;
using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Admin.Legal;

public sealed record CreateLegalDocumentVersionRequest(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content_json")] JsonElement ContentJson,
    [property: JsonPropertyName("content_html")] string ContentHtml,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("is_required")] bool IsRequired,
    [property: JsonPropertyName("block_scope")] string BlockScope);
