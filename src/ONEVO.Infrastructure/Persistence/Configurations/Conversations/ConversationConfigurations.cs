using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Conversations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.SubjectSource).HasMaxLength(40).IsRequired();
        builder.Property(c => c.SubjectKind).HasMaxLength(40).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.SubjectSource, c.SubjectKind, c.SubjectId })
            .IsUnique()
            .HasDatabaseName("ux_conversations_tenant_id_subject_source_subject_kind_subject_id");
    }
}

public class ConversationFavoriteConfiguration : IEntityTypeConfiguration<ConversationFavorite>
{
    public void Configure(EntityTypeBuilder<ConversationFavorite> builder)
    {
        builder.ToTable("conversation_favorites");
        builder.HasKey(f => f.Id);

        builder.HasIndex(f => new { f.EmployeeId, f.ConversationId })
            .IsUnique()
            .HasDatabaseName("ux_conversation_favorites_employee_id_conversation_id");
        builder.HasIndex(f => new { f.TenantId, f.EmployeeId })
            .HasDatabaseName("ix_conversation_favorites_tenant_id_employee_id");

        builder.HasOne<Conversation>().WithMany().HasForeignKey(f => f.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        builder.ToTable("conversation_participants");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.JoinReason).HasMaxLength(20).IsRequired();

        builder.HasIndex(p => new { p.ConversationId, p.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_conversation_participants_conversation_id_employee_id");
        builder.HasIndex(p => new { p.TenantId, p.EmployeeId })
            .HasDatabaseName("ix_conversation_participants_tenant_id_employee_id");

        builder.HasOne<Conversation>().WithMany().HasForeignKey(p => p.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("conversation_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Kind).HasMaxLength(20).IsRequired();
        builder.Property(m => m.BodyHtml).HasMaxLength(10_000).IsRequired();
        builder.Property(m => m.SystemEventJson).HasMaxLength(2000);
        builder.Property(m => m.Tag).HasMaxLength(20);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt })
            .HasDatabaseName("ix_conversation_messages_conversation_id_created_at");

        builder.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConversationMessageMentionConfiguration : IEntityTypeConfiguration<ConversationMessageMention>
{
    public void Configure(EntityTypeBuilder<ConversationMessageMention> builder)
    {
        builder.ToTable("conversation_message_mentions");
        builder.HasKey(m => m.Id);

        builder.HasIndex(m => new { m.MessageId, m.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_conversation_message_mentions_message_id_employee_id");

        builder.HasOne<ConversationMessage>().WithMany().HasForeignKey(m => m.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConversationMessageReactionConfiguration : IEntityTypeConfiguration<ConversationMessageReaction>
{
    public void Configure(EntityTypeBuilder<ConversationMessageReaction> builder)
    {
        builder.ToTable("conversation_message_reactions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Emoji).HasMaxLength(32).IsRequired();

        // One reaction per person per message: reacting again replaces it.
        builder.HasIndex(r => new { r.MessageId, r.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_conversation_message_reactions_message_id_employee_id");

        builder.HasOne<ConversationMessage>().WithMany().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConversationMessageRevisionConfiguration : IEntityTypeConfiguration<ConversationMessageRevision>
{
    public void Configure(EntityTypeBuilder<ConversationMessageRevision> builder)
    {
        builder.ToTable("conversation_message_revisions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.BodyHtml).HasMaxLength(10_000).IsRequired();
        builder.Property(r => r.Kind).HasMaxLength(20).IsRequired();

        builder.HasIndex(r => new { r.MessageId, r.RevisedAt })
            .HasDatabaseName("ix_conversation_message_revisions_message_id_revised_at");

        builder.HasOne<ConversationMessage>().WithMany().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConversationMessageHideConfiguration : IEntityTypeConfiguration<ConversationMessageHide>
{
    public void Configure(EntityTypeBuilder<ConversationMessageHide> builder)
    {
        builder.ToTable("conversation_message_hides");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.MessageId).IsRequired();
        builder.Property(h => h.EmployeeId).IsRequired();
        builder.HasIndex(h => new { h.MessageId, h.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_conversation_message_hides_message_id_employee_id");
        builder.HasIndex(h => new { h.TenantId, h.EmployeeId })
            .HasDatabaseName("ix_conversation_message_hides_tenant_id_employee_id");
        builder.HasOne<ConversationMessage>().WithMany().HasForeignKey(h => h.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConversationMessageAttachmentConfiguration : IEntityTypeConfiguration<ConversationMessageAttachment>
{
    public void Configure(EntityTypeBuilder<ConversationMessageAttachment> builder)
    {
        builder.ToTable("conversation_message_attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.FileName).HasMaxLength(255).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(255).IsRequired();

        builder.HasIndex(a => a.MessageId).HasDatabaseName("ix_conversation_message_attachments_message_id");

        builder.HasOne<ConversationMessage>().WithMany().HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
