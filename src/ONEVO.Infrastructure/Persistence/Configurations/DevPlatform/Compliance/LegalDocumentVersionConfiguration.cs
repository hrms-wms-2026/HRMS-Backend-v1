using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Compliance;

public class LegalDocumentVersionConfiguration : IEntityTypeConfiguration<LegalDocumentVersion>
{
    public void Configure(EntityTypeBuilder<LegalDocumentVersion> builder)
    {
        builder.ToTable("legal_document_versions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContentUrl).HasMaxLength(500);
        builder.Property(x => x.IsRequired).IsRequired();
        builder.Property(x => x.BlockScope).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PublishReason);

        builder.Property(x => x.ContentJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ContentHtml).HasColumnType("text").IsRequired();
        builder.Property(x => x.ContentText).HasColumnType("text").IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();

        builder.HasIndex(x => new { x.DocumentType, x.Version }).IsUnique();

        builder.HasIndex(x => x.DocumentType)
            .IsUnique()
            .HasDatabaseName("ix_legal_document_versions_document_type_published")
            .HasFilter("status = 'published'");

        builder.HasIndex(x => new { x.DocumentType, x.Status, x.IsRequired, x.PublishedAt });

        builder.HasIndex(x => x.ContentHash)
            .HasDatabaseName("ix_legal_document_versions_content_hash");

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(x => x.PublishedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
