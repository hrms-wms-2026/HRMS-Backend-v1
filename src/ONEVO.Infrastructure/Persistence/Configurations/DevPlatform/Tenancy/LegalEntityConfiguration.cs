using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Tenancy;

public class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("legal_entities");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.RegistrationNumber).HasMaxLength(50);
        builder.Property(l => l.CountryCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.AddressJson).HasColumnType("jsonb");
        builder.HasIndex(l => l.TenantId);
    }
}
