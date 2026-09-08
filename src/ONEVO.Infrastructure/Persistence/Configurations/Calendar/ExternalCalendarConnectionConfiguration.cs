using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class ExternalCalendarConnectionConfiguration : IEntityTypeConfiguration<ExternalCalendarConnection>
{
    public void Configure(EntityTypeBuilder<ExternalCalendarConnection> builder)
    {
        builder.ToTable("external_calendar_connections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Provider).HasMaxLength(30).IsRequired();
        builder.Property(c => c.ExternalAccountEmail).HasMaxLength(255).IsRequired();
        builder.Property(c => c.ExternalCalendarId).HasMaxLength(255);
        builder.Property(c => c.ExternalCalendarName).HasMaxLength(255);
        builder.Property(c => c.ScopesJson).HasColumnName("scopes").HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.SyncDirection).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(20).IsRequired();
        builder.Property(c => c.RefreshTokenEncrypted).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.UserId })
            .HasDatabaseName("ix_external_calendar_connections_tenant_id_user_id");

        builder.HasIndex(c => new { c.TenantId, c.UserId, c.Provider })
            .IsUnique()
            .HasDatabaseName("ix_external_calendar_connections_one_per_user_provider");
    }
}
