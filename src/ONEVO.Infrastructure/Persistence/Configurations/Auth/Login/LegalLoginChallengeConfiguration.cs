using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Login;

public class LegalLoginChallengeConfiguration : IEntityTypeConfiguration<LegalLoginChallenge>
{
    public void Configure(EntityTypeBuilder<LegalLoginChallenge> builder)
    {
        builder.ToTable("legal_login_challenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChallengeHash).HasMaxLength(128).IsRequired();
        builder.Property(c => c.CsrfTokenHash).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Origin).HasMaxLength(30).IsRequired();

        // Concurrency token makes consumption single-use, mirroring MfaChallenge/
        // LoginWorkspaceSelectionChallenge: a competing UPDATE that already set consumed_at
        // causes DbUpdateConcurrencyException instead of a silent double-consume.
        builder.Property(c => c.ConsumedAt).IsConcurrencyToken();

        builder.HasIndex(c => c.ChallengeHash).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.UserId, c.ExpiresAt });
        builder.HasIndex(c => c.ExpiresAt);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalLoginChallenge>().WithMany()
            .HasForeignKey(c => c.SupersededById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
