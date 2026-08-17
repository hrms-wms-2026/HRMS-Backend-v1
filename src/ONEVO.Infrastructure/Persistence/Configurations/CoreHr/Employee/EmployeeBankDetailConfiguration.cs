using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Employee;

public class EmployeeBankDetailConfiguration : IEntityTypeConfiguration<EmployeeBankDetail>
{
    public void Configure(EntityTypeBuilder<EmployeeBankDetail> builder)
    {
        builder.ToTable("employee_bank_details");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.BankName).HasColumnName("bank_name").HasMaxLength(100).IsRequired();
        builder.Property(b => b.BranchName).HasColumnName("branch_name").HasMaxLength(100).IsRequired();
        builder.Property(b => b.AccountHolderName).HasColumnName("account_holder_name").HasMaxLength(100).IsRequired();
        builder.Property(b => b.AccountNumberEncrypted).HasColumnName("account_number_encrypted").HasMaxLength(500).IsRequired();
        builder.Property(b => b.AccountType).HasColumnName("account_type").HasMaxLength(30).IsRequired();
        builder.Property(b => b.RoutingNumber).HasColumnName("routing_number").HasMaxLength(20);
        builder.Property(b => b.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(b => b.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.HasIndex(b => new { b.TenantId, b.EmployeeId });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(b => b.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
