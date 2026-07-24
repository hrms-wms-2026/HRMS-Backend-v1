using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class ApplicationCategoryConfiguration : IEntityTypeConfiguration<ApplicationCategory>
{
    public void Configure(EntityTypeBuilder<ApplicationCategory> builder)
    {
        builder.ToTable("application_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ApplicationNamePattern).HasMaxLength(255).IsRequired();
        builder.Property(c => c.Category).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => new { c.TenantId, c.ApplicationNamePattern });
    }
}
