using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Storage.File;

public class FileUploadReservationConfiguration : IEntityTypeConfiguration<FileUploadReservation>
{
    public void Configure(EntityTypeBuilder<FileUploadReservation> builder)
    {
        builder.ToTable("file_upload_reservations");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(r => r.ReservedBytes).HasColumnName("reserved_bytes").IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(r => r.ReservedByUserId).HasColumnName("reserved_by_user_id").IsRequired();
        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(r => r.CompletedFileRecordId).HasColumnName("completed_file_record_id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(r => new { r.TenantId, r.Status, r.ExpiresAt })
            .HasDatabaseName("ix_file_upload_reservations_tenant_id_status_expires_at");

        // Inventory: tenant_id -> tenants.id, reserved_by_user_id -> users.id,
        // completed_file_record_id -> file_records.id (nullable).
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReservedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(r => r.CompletedFileRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
