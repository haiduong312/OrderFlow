using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Messaging;

namespace OrderFlow.Application.Persistence;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> entity)
    {
        entity.ToTable("ProcessedMessages", "messaging", table =>
        {
            table.HasCheckConstraint("CK_Inbox_Metadata", "length(btrim(\"ConsumerName\")) > 0 AND length(btrim(\"MessageType\")) > 0 AND \"SchemaVersion\" > 0 AND octet_length(\"PayloadHash\") = 32 AND jsonb_typeof(\"Payload\") = 'object'");
            table.HasCheckConstraint("CK_Inbox_Attempts", "\"AttemptCount\" >= 0 AND isfinite(\"ReceivedAt\") AND isfinite(\"NextAttemptAt\") AND \"NextAttemptAt\" >= \"ReceivedAt\"");
            table.HasCheckConstraint("CK_Inbox_Status", "\"Status\" IN ('Pending','InFlight','Processed','Failed')");
            table.HasCheckConstraint("CK_Inbox_Lease", "(\"Status\" = 'InFlight' AND \"LeaseOwner\" IS NOT NULL AND length(btrim(\"LeaseOwner\")) > 0 AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\" > \"ReceivedAt\") OR (\"Status\" <> 'InFlight' AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
            table.HasCheckConstraint("CK_Inbox_Processed", "(\"Status\" = 'Processed' AND \"ProcessedAt\" IS NOT NULL AND isfinite(\"ProcessedAt\") AND \"ProcessedAt\" >= \"ReceivedAt\") OR (\"Status\" <> 'Processed' AND \"ProcessedAt\" IS NULL)");
        });
        entity.HasKey(message => new { message.ConsumerName, message.MessageId });
        entity.Property(message => message.ConsumerName).HasMaxLength(100).UseCollation("C").IsRequired();
        entity.Property(message => message.MessageType).HasMaxLength(200).IsRequired();
        entity.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        entity.Property(message => message.PayloadHash).HasColumnType("bytea").IsRequired();
        entity.Property(message => message.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(message => message.LeaseOwner).HasMaxLength(100);
        entity.Property(message => message.LastErrorCode).HasMaxLength(100);
        entity.HasIndex(message => new { message.Status, message.NextAttemptAt, message.ConsumerName, message.MessageId })
            .HasDatabaseName("IX_Inbox_Due");
        entity.HasIndex(message => message.LeaseExpiresAt).HasFilter("\"Status\" = 'InFlight'")
            .HasDatabaseName("IX_Inbox_ExpiredLease");
    }
}
