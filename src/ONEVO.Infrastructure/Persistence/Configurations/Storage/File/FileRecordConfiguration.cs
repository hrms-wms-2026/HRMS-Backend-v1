using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Storage.File;

public class FileRecordConfiguration : IEntityTypeConfiguration<FileRecord>
{
    public void Configure(EntityTypeBuilder<FileRecord> builder)
    {
        builder.ToTable("file_records");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(f => f.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(f => f.StorageKey).HasColumnName("storage_key").HasMaxLength(700).IsRequired();
        builder.Property(f => f.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(f => f.SafeFileName).HasColumnName("safe_file_name").HasMaxLength(255).IsRequired();
        builder.Property(f => f.ContentType).HasColumnName("content_type").HasMaxLength(150).IsRequired();
        builder.Property(f => f.DetectedContentType).HasColumnName("detected_content_type").HasMaxLength(150);
        builder.Property(f => f.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
        builder.Property(f => f.ChecksumSha256).HasColumnName("checksum_sha256").HasMaxLength(64).IsRequired();
        builder.Property(f => f.UploadedByUserId).HasColumnName("uploaded_by_user_id").IsRequired();
        builder.Property(f => f.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(f => f.ScanProvider).HasColumnName("scan_provider").HasMaxLength(50);
        builder.Property(f => f.ScanResultCode).HasColumnName("scan_result_code").HasMaxLength(100);
        builder.Property(f => f.ScanCompletedAt).HasColumnName("scan_completed_at");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.Property(f => f.DeletedAt).HasColumnName("deleted_at");
        builder.Property(f => f.StorageDeletedAt).HasColumnName("storage_deleted_at");

        builder.HasIndex(f => new { f.TenantId, f.Status }).HasDatabaseName("ix_file_records_tenant_id_status");

        // Inventory: file_records.tenant_id -> tenants.id, file_records.uploaded_by_user_id -> users.id.
        // Navigationless FK with Restrict matches the tenant-owned dependent convention
        // used across this backend (e.g. TenantStorageStats, MfaChallenge).
        builder.HasOne<Tenant>().WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
