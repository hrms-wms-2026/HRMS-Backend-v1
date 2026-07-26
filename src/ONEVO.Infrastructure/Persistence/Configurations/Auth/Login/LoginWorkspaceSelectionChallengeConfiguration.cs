using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Login;

public class LoginWorkspaceSelectionChallengeConfiguration : IEntityTypeConfiguration<LoginWorkspaceSelectionChallenge>
{
    public void Configure(EntityTypeBuilder<LoginWorkspaceSelectionChallenge> builder)
    {
        builder.ToTable("login_workspace_selection_challenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChallengeHash).HasMaxLength(128).IsRequired();
        builder.Property(c => c.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(c => c.CandidateWorkspacesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.Purpose).HasMaxLength(40).IsRequired().HasDefaultValue("workspace_selection");
        builder.Property(c => c.FailedAttemptCount).HasDefaultValue(0);
        builder.Property(c => c.IpAddress).HasMaxLength(45);
        builder.Property(c => c.UserAgent).HasMaxLength(500);

        // Concurrency token makes valid-selection consumption single-use, mirroring
        // MfaChallengeConfiguration: a competing UPDATE that already set consumed_at causes
        // DbUpdateConcurrencyException instead of a silent double-consume.
        builder.Property(c => c.ConsumedAt).IsConcurrencyToken();

        builder.HasIndex(c => c.ChallengeHash).IsUnique();
        builder.HasIndex(c => c.ExpiresAt);
        builder.HasIndex(c => new { c.NormalizedEmail, c.CreatedAt });

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_login_workspace_selection_challenges_purpose",
            "purpose = 'workspace_selection'"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_login_workspace_selection_challenges_failed_attempt_count",
            "failed_attempt_count BETWEEN 0 AND 5"));
    }
}
