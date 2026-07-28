using System.Text.Json;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Mappers;

public static class LegalDocumentVersionMapper
{
    public static LegalDocumentVersionSummaryDto ToSummaryDto(LegalDocumentVersion entity)
    {
        return new LegalDocumentVersionSummaryDto(
            entity.Id,
            entity.DocumentType,
            entity.Version,
            entity.Title,
            entity.Status,
            entity.IsRequired,
            entity.BlockScope,
            entity.PublishedAt,
            entity.PublishedById,
            entity.ContentHash,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    public static LegalDocumentVersionDetailDto ToDetailDto(LegalDocumentVersion entity)
    {
        using var contentJsonDocument = JsonDocument.Parse(entity.ContentJson);

        return new LegalDocumentVersionDetailDto(
            entity.Id,
            entity.DocumentType,
            entity.Version,
            entity.Title,
            entity.Status,
            entity.IsRequired,
            entity.BlockScope,
            entity.PublishedAt,
            entity.PublishedById,
            contentJsonDocument.RootElement.Clone(),
            entity.ContentHtml,
            entity.ContentText,
            entity.ContentHash,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    public static PublishedLegalDocumentDto ToPublishedDto(LegalDocumentVersion entity)
    {
        return new PublishedLegalDocumentDto(
            entity.DocumentType,
            entity.Version,
            entity.Title,
            entity.ContentHtml,
            entity.ContentText,
            entity.PublishedAt,
            entity.ContentHash);
    }
}
