using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Provisioning;

public class TenantSetupSelectionConfiguration : IEntityTypeConfiguration<TenantSetupSelection>
{
    public void Configure(EntityTypeBuilder<TenantSetupSelection> builder)
    {
        builder.ToTable("tenant_setup_selections");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.SetupOptionKey).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(50).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.CompletedAt);
        builder.Property(s => s.CompletedById);

        // Unique: same setup option cannot be selected twice for one tenant
        builder.HasIndex(s => new { s.TenantId, s.SetupOptionKey }).IsUnique();
    }
}
