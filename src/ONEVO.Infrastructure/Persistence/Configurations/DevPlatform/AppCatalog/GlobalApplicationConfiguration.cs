using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.AppCatalog;

public sealed class GlobalApplicationConfiguration : IEntityTypeConfiguration<GlobalApplication>
{
    public void Configure(EntityTypeBuilder<GlobalApplication> builder)
    {
        builder.ToTable("global_app_catalog");
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsAllocated);

        builder.Property(x => x.AppName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProcessName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(50);
        builder.Property(x => x.Publisher).HasMaxLength(200);
        builder.Property(x => x.LogoContentType).HasMaxLength(100);
        builder.Property(x => x.Logo).HasColumnType("bytea");

        builder.HasIndex(x => x.ProcessName)
            .IsUnique()
            .HasDatabaseName("ux_global_app_catalog_process_name");
        builder.HasIndex(x => new { x.Category, x.LastSeenAt })
            .HasDatabaseName("ix_global_app_catalog_category_last_seen");
    }
}
