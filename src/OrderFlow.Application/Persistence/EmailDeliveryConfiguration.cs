using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Application.Persistence;

public sealed class EmailDeliveryConfiguration : IEntityTypeConfiguration<EmailDelivery>
{
    public void Configure(EntityTypeBuilder<EmailDelivery> e)
    {
        e.ToTable("EmailDeliveries", "notifications", t =>
        {
            t.HasCheckConstraint("CK_Email_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SourceEventId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Template\"='order-confirmation:v1' AND length(\"ProviderKey\")>0 AND length(\"MessageId\")>0 AND octet_length(\"RequestHash\")=32");
            t.HasCheckConstraint("CK_Email_Status", "\"Status\" IN ('Pending','InFlight','Sent','NeedsReview')");
            t.HasCheckConstraint("CK_Email_Counts", "\"AttemptCount\">=0 AND \"AttemptsInCycle\" BETWEEN 0 AND 8 AND \"AttemptsInCycle\"<=\"AttemptCount\" AND \"ReviewRetryCount\">=0");
            t.HasCheckConstraint("CK_Email_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND isfinite(\"NextAttemptAt\") AND \"UpdatedAt\">=\"CreatedAt\" AND \"NextAttemptAt\">=\"CreatedAt\" AND (\"LastFailureAt\" IS NULL OR (isfinite(\"LastFailureAt\") AND \"LastFailureAt\">=\"CreatedAt\"))");
            t.HasCheckConstraint("CK_Email_Sent", "(\"Status\"='Sent' AND \"SentAt\" IS NOT NULL AND isfinite(\"SentAt\") AND \"SentAt\">=\"CreatedAt\" AND NOT \"OutcomeUncertain\") OR (\"Status\"<>'Sent' AND \"SentAt\" IS NULL)");
            t.HasCheckConstraint("CK_Email_Lease", "(\"Status\"='InFlight' AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\">\"CreatedAt\" AND \"OutcomeUncertain\") OR (\"Status\"<>'InFlight' AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
        });
        e.HasKey(d => d.Id); e.Property(d => d.Id).ValueGeneratedNever();
        e.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
        e.Property(d => d.RecipientEmail).HasMaxLength(256); e.Property(d => d.FromEmail).HasMaxLength(256);
        e.Property(d => d.Template).HasMaxLength(100); e.Property(d => d.Subject).HasMaxLength(200); e.Property(d => d.TextBody).HasMaxLength(4000);
        e.Property(d => d.Provider).HasMaxLength(64); e.Property(d => d.ProviderKey).HasMaxLength(100); e.Property(d => d.MessageId).HasMaxLength(100);
        e.Property(d => d.RequestHash).HasColumnType("bytea"); e.Property(d => d.LeaseOwner).HasMaxLength(100); e.Property(d => d.LeaseToken).IsConcurrencyToken();
        e.Property(d => d.LastErrorCode).HasMaxLength(100); e.Property(d => d.ProviderMessageId).HasMaxLength(200);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Order>().WithMany().HasForeignKey(d => d.OrderId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(d => new { d.SourceEventId, d.Template, d.RecipientUserId }).IsUnique().HasDatabaseName("UX_Email_Source_Template_Recipient");
        e.HasIndex(d => new { d.OrderId, d.Template }).IsUnique().HasDatabaseName("UX_Email_Order_Template");
        e.HasIndex(d => new { d.Provider, d.ProviderKey }).IsUnique();
        e.HasIndex(d => new { d.Status, d.NextAttemptAt, d.Id }).HasDatabaseName("IX_Email_Due");
        e.HasIndex(d => d.LeaseExpiresAt).HasFilter("\"Status\"='InFlight'").HasDatabaseName("IX_Email_Lease");
        foreach (var n in new[] { nameof(EmailDelivery.SourceEventId), nameof(EmailDelivery.OrderId), nameof(EmailDelivery.RecipientUserId),
            nameof(EmailDelivery.RecipientEmail), nameof(EmailDelivery.FromEmail), nameof(EmailDelivery.Template), nameof(EmailDelivery.Subject), nameof(EmailDelivery.TextBody),
            nameof(EmailDelivery.Provider), nameof(EmailDelivery.ProviderKey), nameof(EmailDelivery.MessageId), nameof(EmailDelivery.RequestHash), nameof(EmailDelivery.CreatedAt) })
            e.Property(n).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
public sealed class EmailDeliveryRetryConfiguration : IEntityTypeConfiguration<EmailDeliveryRetry>
{
    public void Configure(EntityTypeBuilder<EmailDeliveryRetry> e)
    {
        e.ToTable("EmailDeliveryRetries", "notifications", t => t.HasCheckConstraint("CK_EmailRetry_Metadata", "\"CycleNumber\">0 AND length(btrim(\"Reason\")) BETWEEN 1 AND 300 AND isfinite(\"RequestedAt\") AND \"OperationId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        e.HasKey(r => r.OperationId); e.Property(r => r.OperationId).ValueGeneratedNever(); e.Property(r => r.Reason).HasMaxLength(300);
        e.HasOne<EmailDelivery>().WithMany().HasForeignKey(r => r.DeliveryId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(r => new { r.DeliveryId, r.CycleNumber }).IsUnique();
        foreach (var p in e.Metadata.GetProperties()) p.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
