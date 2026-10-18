using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Jobs.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Jobs;

public class JobLeaseConfiguration : IEntityTypeConfiguration<JobLease>
{
    public void Configure(EntityTypeBuilder<JobLease> builder)
    {
        builder.ToTable("job_leases");
        builder.HasKey(l => l.JobName);
        builder.Property(l => l.JobName).HasMaxLength(100);
        builder.Property(l => l.OwnerInstanceId).HasMaxLength(100).IsRequired();
    }
}
