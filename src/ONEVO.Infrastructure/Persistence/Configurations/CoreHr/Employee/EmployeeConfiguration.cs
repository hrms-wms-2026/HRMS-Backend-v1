using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Employee;

public class EmployeeConfiguration : IEntityTypeConfiguration<EmployeeEntity>
{
    public void Configure(EntityTypeBuilder<EmployeeEntity> builder)
    {
        builder.ToTable("employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EmployeeNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.LastName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(255).IsRequired();
        builder.Property(e => e.Phone).HasMaxLength(20);
        builder.Property(e => e.Gender).HasMaxLength(10);
        builder.Property(e => e.EmploymentTypeId).IsRequired();
        builder.Property(e => e.EmploymentStatusId).IsRequired();
        builder.Property(e => e.WorkModeId).IsRequired();
        builder.Property(e => e.DisplayTimezone).HasMaxLength(50);

        // Concurrency token mapped to the PostgreSQL system column xmin - see
        // OnboardingDraftConfiguration.cs for the identical precedent and rationale. Declared
        // nullable (uint?, not uint) so EF does not emit a NOT NULL constraint: PostgreSQL always
        // populates its own xmin system column regardless of this metadata, but
        // DevSmokeTestTenantSeederTests exercises a real non-PostgreSQL unit-test database schema
        // via Database.EnsureCreated() (unlike OnboardingDraft, whose xmin column that test never
        // inserts through), and that provider has no such system column - a NOT NULL "xmin" there
        // would reject every insert.
        builder.Property<uint?>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasIndex(e => new { e.TenantId, e.EmployeeNumber }).IsUnique();
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => new { e.UserId, e.LegalEntityId }).IsUnique();
    }
}
