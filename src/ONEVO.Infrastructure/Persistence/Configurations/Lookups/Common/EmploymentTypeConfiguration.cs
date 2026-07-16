using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Lookups;

namespace ONEVO.Infrastructure.Persistence.Configurations.Lookups.Common;

public class EmploymentTypeConfiguration : IEntityTypeConfiguration<EmploymentType>
{
    public void Configure(EntityTypeBuilder<EmploymentType> builder)
    {
        builder.ToTable("employment_types");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();
    }
}
