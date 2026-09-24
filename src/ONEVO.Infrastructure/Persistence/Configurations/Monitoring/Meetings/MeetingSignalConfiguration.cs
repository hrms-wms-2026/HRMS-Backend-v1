using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Meetings;

public class MeetingSignalConfiguration : IEntityTypeConfiguration<MeetingSignal>
{
    public void Configure(EntityTypeBuilder<MeetingSignal> builder)
    {
        builder.ToTable("meeting_signals");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_meeting_signals_tenant_employee_captured");
    }
}
