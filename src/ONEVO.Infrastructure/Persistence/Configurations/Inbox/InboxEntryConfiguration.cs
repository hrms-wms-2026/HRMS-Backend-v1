using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Inbox;

/// <summary>Task 37: the inbox header table. Indexes answer the two inbox questions (sent, and via the assignees
/// table, received) without touching the module tables.</summary>
public class InboxEntryConfiguration : IEntityTypeConfiguration<InboxEntry>
{
    public void Configure(EntityTypeBuilder<InboxEntry> builder)
    {
        builder.ToTable("inbox_items", t =>
        {
            t.HasCheckConstraint("ck_inbox_items_status", InList("status", InboxEntryStatuses.All));
            t.HasCheckConstraint("ck_inbox_items_category", InList("category", InboxEntryCategories.All));
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Source).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Kind).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Category).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Summary).HasMaxLength(400);

        builder.HasIndex(e => new { e.TenantId, e.Source, e.Kind, e.SubjectId })
            .IsUnique()
            .HasDatabaseName("ux_inbox_items_tenant_id_source_kind_subject_id");
        builder.HasIndex(e => new { e.TenantId, e.RequestedByEmployeeId, e.Status, e.UpdatedAt })
            .IsDescending(false, false, false, true)
            .HasDatabaseName("ix_inbox_items_tenant_id_requested_by_status_updated_at");

        builder.HasMany(e => e.Assignees)
            .WithOne()
            .HasForeignKey(a => a.InboxEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    internal static string InList(string column, IEnumerable<string> values)
        => $"{column} IN ({string.Join(", ", values.Select(v => $"'{v}'"))})";
}

public class InboxEntryAssigneeConfiguration : IEntityTypeConfiguration<InboxEntryAssignee>
{
    public void Configure(EntityTypeBuilder<InboxEntryAssignee> builder)
    {
        builder.ToTable("inbox_item_assignees", t =>
            t.HasCheckConstraint("ck_inbox_item_assignees_status", InboxEntryConfiguration.InList("status", InboxEntryAssigneeStatuses.All)));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Source).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Kind).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(20).IsRequired();

        builder.HasIndex(e => new { e.InboxEntryId, e.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_inbox_item_assignees_inbox_entry_id_employee_id");
        // "Received": what this person must act on, newest first; the counts group it by source and kind.
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Status, e.UpdatedAt })
            .IsDescending(false, false, false, true)
            .IncludeProperties(e => new { e.Source, e.Kind, e.InboxEntryId })
            .HasDatabaseName("ix_inbox_item_assignees_tenant_id_employee_id_status_updated_at");
    }
}
