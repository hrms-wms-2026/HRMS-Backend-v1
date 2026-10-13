using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Inbox;

public class InboxDecisionJournalEntryConfiguration : IEntityTypeConfiguration<InboxDecisionJournalEntry>
{
    public void Configure(EntityTypeBuilder<InboxDecisionJournalEntry> builder)
    {
        builder.ToTable("inbox_decision_journal");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Source).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Kind).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(60).IsRequired();
        builder.Property(e => e.BeforeJson).HasColumnType("jsonb");
        builder.Property(e => e.AfterJson).HasColumnType("jsonb");
        builder.Property(e => e.Comment).HasMaxLength(4000);

        builder.HasIndex(e => new { e.TenantId, e.Source, e.Kind, e.ItemId, e.DecidedAt })
            .HasDatabaseName("ix_inbox_decision_journal_tenant_id_source_kind_item_id_decided_at");
        builder.HasIndex(e => new { e.TenantId, e.ActorEmployeeId, e.DecidedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_inbox_decision_journal_tenant_id_actor_employee_id_decided_at");
    }
}
