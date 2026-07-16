using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Tenancy;

public class TenantStatusHistoryConfiguration : IEntityTypeConfiguration<TenantStatusHistory>
{
    public void Configure(EntityTypeBuilder<TenantStatusHistory> builder)
    {
        builder.ToTable("tenant_status_histories");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.Reason).HasMaxLength(500);
        builder.HasIndex(h => h.TenantId);
        builder.HasIndex(h => h.ChangedAt);
    }
}
