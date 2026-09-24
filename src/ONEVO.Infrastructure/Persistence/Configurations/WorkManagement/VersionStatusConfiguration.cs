using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class VersionStatusConfiguration : IEntityTypeConfiguration<VersionStatus>
{
    public void Configure(EntityTypeBuilder<VersionStatus> builder)
    {
        builder.ToTable("version_statuses");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Label).HasMaxLength(50).IsRequired();

        builder.HasIndex(s => s.Code).IsUnique().HasDatabaseName("ix_version_statuses_code");
    }
}
