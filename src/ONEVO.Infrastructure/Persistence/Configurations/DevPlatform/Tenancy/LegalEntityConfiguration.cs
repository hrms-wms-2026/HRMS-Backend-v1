using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Tenancy;

public class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable(
            "legal_entities",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_legal_entities_office_coordinates",
                    "(office_latitude IS NULL AND office_longitude IS NULL) OR " +
                    "(office_latitude BETWEEN -90 AND 90 AND office_longitude BETWEEN -180 AND 180)");
                table.HasCheckConstraint(
                    "ck_legal_entities_office_radius",
                    "office_allowed_radius_meters IS NULL OR " +
                    "office_allowed_radius_meters BETWEEN 25 AND 50000");
                table.HasCheckConstraint(
                    "ck_legal_entities_timezone",
                    "length(trim(timezone)) > 0");
            });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.RegistrationNumber).HasMaxLength(50);
        builder.Property(l => l.CountryCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.AddressJson).HasColumnType("jsonb");
        builder.Property(l => l.OfficeAddressLabel).HasMaxLength(255);
        builder.Property(l => l.OfficeLatitude).HasPrecision(10, 7);
        builder.Property(l => l.OfficeLongitude).HasPrecision(10, 7);
        builder.Property(l => l.OfficeAllowedRadiusMeters);
        builder.Property(l => l.Timezone)
            .HasMaxLength(50)
            .HasDefaultValue("UTC")
            .IsRequired();
        builder.HasIndex(l => l.TenantId);
    }
}
