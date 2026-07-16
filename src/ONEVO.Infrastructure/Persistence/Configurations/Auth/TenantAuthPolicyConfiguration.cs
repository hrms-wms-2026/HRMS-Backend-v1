using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth;

public class TenantAuthPolicyConfiguration : IEntityTypeConfiguration<TenantAuthPolicy>
{
    public void Configure(EntityTypeBuilder<TenantAuthPolicy> builder)
    {
        builder.ToTable("tenant_auth_policies");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.TenantId).IsUnique();

        builder.Property(x => x.AllowedLoginDomainsJson).HasColumnType("text");
    }
}
