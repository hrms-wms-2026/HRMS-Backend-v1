using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Login;

public class UserExternalIdentityConfiguration : IEntityTypeConfiguration<UserExternalIdentity>
{
    public void Configure(EntityTypeBuilder<UserExternalIdentity> builder)
    {
        builder.ToTable("user_external_identities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderSubject).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProviderEmail).HasMaxLength(254).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Provider, x.ProviderSubject }).IsUnique();
        builder.HasIndex(x => x.UserId);
    }
}
