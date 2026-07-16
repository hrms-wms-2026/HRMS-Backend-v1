using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Tenancy;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(100).IsRequired();
        builder.Property(t => t.IndustryProfile).HasMaxLength(30).IsRequired();
        builder.Property(t => t.CompanySizeRange).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(t => t.SettingsJson).HasColumnType("jsonb");
        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
