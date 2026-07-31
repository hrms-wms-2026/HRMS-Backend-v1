using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.TrayActivation;

public class TrayActivationCodeConfiguration : IEntityTypeConfiguration<TrayActivationCode>
{
    public void Configure(EntityTypeBuilder<TrayActivationCode> builder)
    {
        builder.ToTable("tray_activation_codes");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.TenantId).IsRequired();

        builder.HasIndex(t => t.CodeHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.TenantId, t.CreatedAt });

        builder.Ignore(t => t.IsValid);
    }
}
