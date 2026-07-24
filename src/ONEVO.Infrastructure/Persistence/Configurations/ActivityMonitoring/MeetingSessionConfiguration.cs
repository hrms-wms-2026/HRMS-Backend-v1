using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class MeetingSessionConfiguration : IEntityTypeConfiguration<MeetingSession>
{
    public void Configure(EntityTypeBuilder<MeetingSession> builder)
    {
        builder.ToTable("meeting_sessions");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Platform).HasMaxLength(20).IsRequired();
        builder.HasIndex(m => new { m.TenantId, m.EmployeeId, m.MeetingStart });
    }
}
