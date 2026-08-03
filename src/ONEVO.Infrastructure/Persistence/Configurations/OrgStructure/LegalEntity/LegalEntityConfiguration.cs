using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

// Namespace deliberately stops at the feature segment: a ".LegalEntity" segment would
// collide with the LegalEntity entity type and force using-aliases everywhere (same
// convention as PositionConfiguration).
namespace ONEVO.Infrastructure.Persistence.Configurations.OrgStructure;

public class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("legal_entities", table =>
        {
            table.HasCheckConstraint(
                "ck_legal_entities_financial_year_start_month",
                "financial_year_start_month BETWEEN 1 AND 12");
            table.HasCheckConstraint(
                "ck_legal_entities_first_day_of_week",
                "first_day_of_week BETWEEN 1 AND 7");
            table.HasCheckConstraint(
                "ck_legal_entities_time_format",
                "time_format IN ('12h', '24h')");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.RegistrationNumber).HasMaxLength(50);
        builder.Property(l => l.CountryCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();

        // Registered business address payload. This was already the existing
        // "address_json" column before Part 2A; the finalized screen design
        // calls this field "registered business address" but there is no
        // audit/doc evidence that renaming the column is required or safe, so
        // it is preserved under its existing name (Part 1 audit + Part 2A
        // scope instruction: keep and document rather than rename on doubt).
        builder.Property(l => l.AddressJson).HasColumnType("jsonb");

        builder.Property(l => l.CompanyCode).HasMaxLength(20);
        builder.Property(l => l.TaxRegistrationNumber).HasMaxLength(80);
        builder.Property(l => l.VatGstNumber).HasMaxLength(50);
        builder.Property(l => l.Email).HasMaxLength(254);
        builder.Property(l => l.PhoneNumber).HasMaxLength(20);
        builder.Property(l => l.Website).HasMaxLength(255);
        builder.Property(l => l.Timezone).HasMaxLength(50);
        builder.Property(l => l.FinancialYearStartMonth).IsRequired().HasDefaultValue(1);
        builder.Property(l => l.FirstDayOfWeek).IsRequired().HasDefaultValue(1);

        // JSON array of weekday numbers using the 1=Monday..7=Sunday convention
        // (matches AddressJson's existing raw-JSON-string pattern rather than a
        // typed List<int> + value comparer, to keep the entity consistent with
        // how the codebase already stores flexible JSON settings).
        builder.Property(l => l.StandardWorkingDays)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("[1,2,3,4,5]");

        builder.Property(l => l.DefaultLanguage).HasMaxLength(10).IsRequired().HasDefaultValue("en-US");
        builder.Property(l => l.DateFormat).HasMaxLength(20).IsRequired().HasDefaultValue("DD MMM YYYY");
        builder.Property(l => l.TimeFormat).HasMaxLength(10).IsRequired().HasDefaultValue("12h");

        // Existing index, preserved as-is.
        builder.HasIndex(l => l.TenantId);

        // New tenant-scoped uniqueness. Name is always non-null today, so this
        // is a plain unique index; company_code and registration_number are
        // optional today, so partial indexes (WHERE ... IS NOT NULL) keep
        // every existing NULL-valued row non-conflicting.
        builder.HasIndex(l => new { l.TenantId, l.Name })
            .IsUnique()
            .HasDatabaseName("ix_legal_entities_tenant_id_name");
        builder.HasIndex(l => new { l.TenantId, l.CompanyCode })
            .IsUnique()
            .HasFilter("company_code IS NOT NULL")
            .HasDatabaseName("ix_legal_entities_tenant_id_company_code");
        builder.HasIndex(l => new { l.TenantId, l.RegistrationNumber })
            .IsUnique()
            .HasFilter("registration_number IS NOT NULL")
            .HasDatabaseName("ix_legal_entities_tenant_id_registration_number");

        // Self-referencing parent company. Nullable, and Restrict (never
        // cascade) so deleting/deactivating a company that still has children
        // fails loudly instead of silently taking the children down with it -
        // matches the TenantStatusHistory -> Tenant FK convention.
        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(l => l.ParentLegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Logo reference. Deliberate hardening beyond the fields the task
        // literally mandated an FK for (only parent_legal_entity_id was
        // required to be an FK): legal_entities had zero FKs before this
        // migration, and file_records already models soft-deletion, so
        // SetNull lets a file record be cleaned up independently without
        // blocking or cascading into legal_entities.
        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(l => l.LogoFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
