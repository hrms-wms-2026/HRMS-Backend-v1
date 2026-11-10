using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Meetings;

public class MeetingDelegateConfiguration : IEntityTypeConfiguration<MeetingDelegate>
{
    public void Configure(EntityTypeBuilder<MeetingDelegate> builder)
    {
        builder.ToTable("meeting_delegates");
        builder.HasKey(d => d.Id);

        // One unrevoked grant per principal and delegate; revoked rows stay for audit.
        builder.HasIndex(d => new { d.TenantId, d.PrincipalEmployeeId, d.DelegateEmployeeId })
            .IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ux_meeting_delegates_pair_unrevoked");
        // "Whom may I act for" is read on every delegated request.
        builder.HasIndex(d => new { d.TenantId, d.DelegateEmployeeId })
            .HasDatabaseName("ix_meeting_delegates_delegate");

        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.PrincipalEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.DelegateEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
