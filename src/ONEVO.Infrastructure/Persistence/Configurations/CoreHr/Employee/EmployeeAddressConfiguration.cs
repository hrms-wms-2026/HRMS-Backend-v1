using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Employee;

public class EmployeeAddressConfiguration : IEntityTypeConfiguration<EmployeeAddress>
{
    public void Configure(EntityTypeBuilder<EmployeeAddress> builder)
    {
        builder.ToTable("employee_addresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AddressType).HasColumnName("address_type").HasMaxLength(20).IsRequired();
        builder.Property(a => a.AddressJson).HasColumnName("address_json").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(a => a.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
