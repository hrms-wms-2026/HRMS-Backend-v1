using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Storage;

public class EntityAssetConfiguration : IEntityTypeConfiguration<EntityAsset>
{
    public void Configure(EntityTypeBuilder<EntityAsset> builder)
    {
        builder.ToTable("entity_assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.OwnerType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.AssetPurpose).HasMaxLength(50).IsRequired();
        builder.Property(a => a.MetadataJson).HasColumnType("jsonb");
        builder.Property(a => a.CreatedByType).HasMaxLength(30).IsRequired();

        builder.HasIndex(a => new { a.OwnerType, a.OwnerId });

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(a => a.FileRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
