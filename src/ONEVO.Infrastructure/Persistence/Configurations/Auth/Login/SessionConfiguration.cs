using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Login;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.IpAddress).HasMaxLength(45);
        builder.Property(s => s.UserAgent).HasMaxLength(500);
        builder.Property(s => s.CsrfTokenHash).HasMaxLength(128);
        builder.Property(s => s.KeyHash).HasMaxLength(128).IsRequired();

        builder.HasIndex(s => new { s.UserId, s.IsRevoked });
        builder.HasIndex(s => s.TenantId);
        builder.HasIndex(s => s.KeyHash).IsUnique();
    }
}
