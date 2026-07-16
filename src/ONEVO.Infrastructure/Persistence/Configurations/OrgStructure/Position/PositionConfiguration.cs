using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".Position" segment would
// collide with the Position entity type and force using-aliases everywhere.
namespace ONEVO.Infrastructure.Persistence.Configurations.OrgStructure;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
    }
}
