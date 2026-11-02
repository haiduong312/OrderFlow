using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Messaging;

namespace OrderFlow.Application.Persistence;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable("OutboxMessages", "messaging", table =>
        {
            table.HasCheckConstraint("CK_Outbox_Metadata", "length(btrim(\"EventType\")) > 0 AND length(btrim(\"Producer\")) > 0 AND length(btrim(\"AggregateType\")) > 0 AND \"SchemaVersion\" > 0 AND \"AggregateVersion\" > 0 AND jsonb_typeof(\"Payload\") = 'object'");
            table.HasCheckConstraint("CK_Outbox_Attempts", "\"AttemptCount\" >= 0 AND isfinite(\"OccurredAt\") AND isfinite(\"NextAttemptAt\") AND \"NextAttemptAt\" >= \"OccurredAt\"");
            table.HasCheckConstraint("CK_Outbox_Status", "\"Status\" IN ('Pending','InFlight','Published','Failed')");
            table.HasCheckConstraint("CK_Outbox_Lease", "(\"Status\" = 'InFlight' AND \"LeaseOwner\" IS NOT NULL AND length(btrim(\"LeaseOwner\")) > 0 AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\" > \"OccurredAt\") OR (\"Status\" <> 'InFlight' AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
            table.HasCheckConstraint("CK_Outbox_Published", "(\"Status\" = 'Published' AND \"PublishedAt\" IS NOT NULL AND isfinite(\"PublishedAt\") AND \"PublishedAt\" >= \"OccurredAt\") OR (\"Status\" <> 'Published' AND \"PublishedAt\" IS NULL)");
        });
        entity.HasKey(message => message.EventId);
        entity.Property(message => message.EventId).ValueGeneratedNever();
        entity.Property(message => message.EventType).HasMaxLength(200).IsRequired();
        entity.Property(message => message.Producer).HasMaxLength(64).IsRequired();
        entity.Property(message => message.AggregateType).HasMaxLength(100).IsRequired();
        entity.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        entity.Property(message => message.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(message => message.LeaseOwner).HasMaxLength(100);
        entity.Property(message => message.LastErrorCode).HasMaxLength(100);
        entity.HasIndex(message => new { message.Status, message.NextAttemptAt, message.EventId })
            .HasDatabaseName("IX_Outbox_Due");
        entity.HasIndex(message => message.LeaseExpiresAt).HasFilter("\"Status\" = 'InFlight'")
            .HasDatabaseName("IX_Outbox_ExpiredLease");
        entity.HasIndex(message => new { message.Producer, message.AggregateType, message.AggregateId, message.AggregateVersion, message.EventType })
            .IsUnique().HasDatabaseName("UX_Outbox_ProducerEffect");
    }
}
