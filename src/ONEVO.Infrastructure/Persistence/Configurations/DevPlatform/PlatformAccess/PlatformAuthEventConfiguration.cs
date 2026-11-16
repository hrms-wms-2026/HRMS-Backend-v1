using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public class PlatformAuthEventConfiguration : IEntityTypeConfiguration<PlatformAuthEvent>
{
    public void Configure(EntityTypeBuilder<PlatformAuthEvent> builder)
    {
        builder.ToTable("platform_auth_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasMaxLength(80).IsRequired();
        builder.Property(e => e.SourceIp).HasMaxLength(45);
        builder.Property(e => e.MetadataJson).IsJsonColumn();

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.CreatedAt);

        // Auth events are append-only: deleting a referenced platform user must fail, never null the
        // column (a cascade is not permission-checked, so it would bypass the DENY).
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
