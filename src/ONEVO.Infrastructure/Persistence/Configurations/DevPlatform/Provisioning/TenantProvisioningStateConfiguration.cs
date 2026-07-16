using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Provisioning;

public class TenantProvisioningStateConfiguration : IEntityTypeConfiguration<TenantProvisioningState>
{
    public void Configure(EntityTypeBuilder<TenantProvisioningState> builder)
    {
        builder.ToTable("tenant_provisioning_states");
        builder.HasKey(p => p.TenantId);
        builder.Property(p => p.CurrentStep).HasMaxLength(50).IsRequired();
    }
}
