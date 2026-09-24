using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Employee;

public class EmployeeDependentConfiguration : IEntityTypeConfiguration<EmployeeDependent>
{
    public void Configure(EntityTypeBuilder<EmployeeDependent> builder)
    {
        builder.ToTable("employee_dependents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(d => d.Relationship).HasColumnName("relationship").HasMaxLength(20).IsRequired();
        builder.Property(d => d.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date").IsRequired();
        builder.Property(d => d.IsEmergencyContact).HasColumnName("is_emergency_contact").IsRequired();
        builder.Property(d => d.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(d => d.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.HasIndex(d => new { d.TenantId, d.EmployeeId });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
