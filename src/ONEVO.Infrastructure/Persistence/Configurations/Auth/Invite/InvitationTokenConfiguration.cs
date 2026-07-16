using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Invite;

public class InvitationTokenConfiguration : IEntityTypeConfiguration<InvitationToken>
{
    public void Configure(EntityTypeBuilder<InvitationToken> builder)
    {
        builder.ToTable("invitation_tokens");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvitedEmail).HasMaxLength(254).IsRequired();
        builder.Property(i => i.InvitedFullName).HasMaxLength(255).IsRequired();
        builder.Property(i => i.Status).HasMaxLength(20).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(128).IsRequired();

        builder.Property(i => i.CompletedWith).HasMaxLength(20);
        builder.Property(i => i.CompletionMethodsJson).HasColumnType("jsonb");
        builder.Property(i => i.AllowedEmailDomainsJson).HasColumnType("jsonb");
        builder.Property(i => i.PositionId);

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.InvitedEmail });
        builder.HasIndex(i => i.ExpiresAt);
    }
}
