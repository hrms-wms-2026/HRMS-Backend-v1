using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Permission;

public class PermissionChangeConfiguration : IEntityTypeConfiguration<PermissionChange>
{
    public void Configure(EntityTypeBuilder<PermissionChange> builder)
    {
        builder.ToTable("permission_changes");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChangeKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(c => c.ChangeKey).IsUnique();

        builder.Property(c => c.Kind).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        builder.Property(c => c.PreviousCode).HasMaxLength(50);
        builder.Property(c => c.Detail).IsJsonColumn().IsRequired();
        builder.Property(c => c.AppliedAt).IsRequired();
    }
}
