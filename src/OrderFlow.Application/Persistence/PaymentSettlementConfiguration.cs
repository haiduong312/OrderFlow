using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;

namespace OrderFlow.Application.Persistence;

public sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> b)
    {
        b.ToTable("PaymentEvents", "payments", t =>
        {
            t.HasCheckConstraint("CK_PaymentEvents_Payload", "\"Provider\" = 'Simulator' AND octet_length(\"RawBody\") BETWEEN 1 AND 65536 AND octet_length(\"PayloadHash\") = 32");
            t.HasCheckConstraint("CK_PaymentEvents_State", "(\"Status\" = 'Pending' AND \"DecisionAt\" IS NULL AND \"ErrorCode\" IS NULL) OR (\"Status\" = 'Processed' AND \"DecisionAt\" IS NOT NULL AND \"ErrorCode\" IS NULL) OR (\"Status\" = 'Quarantined' AND \"DecisionAt\" IS NOT NULL AND \"ErrorCode\" IS NOT NULL)");
            t.HasCheckConstraint("CK_PaymentEvents_Time", "isfinite(\"OccurredAt\") AND isfinite(\"ReceivedAt\") AND (\"DecisionAt\" IS NULL OR (isfinite(\"DecisionAt\") AND \"DecisionAt\" >= \"ReceivedAt\"))");
        });
        b.HasKey(e => new { e.Provider, e.ProviderEventId });
        b.Property(e => e.Provider).HasMaxLength(32).UseCollation("C");
        b.Property(e => e.RawBody).HasColumnType("bytea"); b.Property(e => e.PayloadHash).HasColumnType("bytea");
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.ErrorCode).HasMaxLength(100);
        b.HasIndex(e => new { e.ReceivedAt, e.ProviderEventId }).HasFilter("\"Status\" = 'Pending'");
        foreach (var name in new[] { nameof(PaymentEvent.RawBody), nameof(PaymentEvent.PayloadHash), nameof(PaymentEvent.ReferencedOrderId), nameof(PaymentEvent.OccurredAt), nameof(PaymentEvent.ReceivedAt) })
            b.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class PaymentCaptureConfiguration : IEntityTypeConfiguration<PaymentCapture>
{
    public void Configure(EntityTypeBuilder<PaymentCapture> b)
    {
        b.ToTable("PaymentCaptures", "payments", t =>
        {
            t.HasCheckConstraint("CK_PaymentCaptures_Source", "(\"Source\" = 'Callback' AND \"ProviderEventId\" IS NOT NULL AND \"ProviderOccurredAt\" IS NOT NULL) OR (\"Source\" = 'ProviderLookup' AND \"ProviderEventId\" IS NULL AND \"ProviderOccurredAt\" IS NULL)");
            t.HasCheckConstraint("CK_PaymentCaptures_Facts", "\"Provider\" = 'Simulator' AND length(\"ProviderCaptureId\") > 0 AND length(\"ProviderTransactionId\") > 0 AND \"Amount\" > 0 AND \"Currency\" = 'USD' AND (\"ProviderOccurredAt\" IS NULL OR isfinite(\"ProviderOccurredAt\")) AND isfinite(\"RecordedAt\")");
        });
        b.HasKey(c => c.Id); b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Provider).HasMaxLength(32).UseCollation("C");
        b.Property(c => c.ProviderCaptureId).HasMaxLength(100).UseCollation("C");
        b.Property(c => c.ProviderTransactionId).HasMaxLength(100).UseCollation("C");
        b.Property(c => c.Currency).HasMaxLength(3);
        b.Property(c => c.Source).HasMaxLength(16).HasDefaultValue("Callback");
        b.HasIndex(c => new { c.Provider, c.ProviderCaptureId }).IsUnique();
        b.HasIndex(c => c.OrderId).IsUnique().HasFilter("\"Allocated\"").HasDatabaseName("UX_PaymentCaptures_AllocatedOrder");
        b.HasOne<PaymentAttempt>().WithMany().HasForeignKey(c => c.AttemptId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentEvent>().WithMany().HasForeignKey(c => new { c.Provider, c.ProviderEventId }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in b.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> b)
    {
        b.ToTable("Refunds", "payments", t =>
        {
            t.HasCheckConstraint("CK_Refunds_Delivery", "\"DeliveryCount\" BETWEEN 0 AND 8 AND isfinite(\"UpdatedAt\") AND \"UpdatedAt\" >= \"CreatedAt\" AND isfinite(\"NextAttemptAt\") AND \"NextAttemptAt\" >= \"CreatedAt\"");
            t.HasCheckConstraint("CK_Refunds_Lease", "(\"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL) OR (\"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\" > \"UpdatedAt\" AND \"DeliveryCount\" > 0)");
            t.HasCheckConstraint("CK_Refunds_Outcome", "(\"Status\" = 'Succeeded' AND \"ProviderRefundId\" IS NOT NULL AND length(\"ProviderRefundId\") > 0 AND \"SucceededAt\" IS NOT NULL AND isfinite(\"SucceededAt\") AND \"SucceededAt\" >= \"CreatedAt\" AND \"LeaseToken\" IS NULL AND \"LastErrorCode\" IS NULL) OR (\"Status\" IN ('Pending','NeedsReview') AND \"ProviderRefundId\" IS NULL AND \"SucceededAt\" IS NULL AND (\"Status\" <> 'NeedsReview' OR \"LastErrorCode\" IS NOT NULL))");
            t.HasCheckConstraint("CK_Refunds_Intention", "\"Provider\" = 'Simulator' AND \"Purpose\" = 'FullCompensation' AND \"Status\" IN ('Pending','Succeeded','NeedsReview') AND \"Currency\" = 'USD' AND \"Amount\" > 0 AND isfinite(\"CreatedAt\") AND length(\"Reason\") > 0 AND \"ProviderRefundKey\" = 'refund-' || replace(\"Id\"::text,'-','')");
        });
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Provider).HasMaxLength(32).UseCollation("C");
        b.Property(r => r.ProviderCaptureId).HasMaxLength(100).UseCollation("C");
        b.Property(r => r.Purpose).HasMaxLength(32); b.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.ProviderRefundKey).HasMaxLength(80).UseCollation("C");
        b.Property(r => r.Reason).HasMaxLength(100); b.Property(r => r.Currency).HasMaxLength(3);
        b.HasIndex(r => new { r.Provider, r.ProviderCaptureId, r.Purpose }).IsUnique();
        b.HasIndex(r => r.ProviderRefundKey).IsUnique(); b.HasIndex(r => r.CaptureId).IsUnique();
        b.HasOne<PaymentCapture>().WithOne().HasForeignKey<PaymentRefund>(r => r.CaptureId).OnDelete(DeleteBehavior.Restrict);
        b.Property(r => r.ProviderRefundId).HasMaxLength(100).UseCollation("C");
        b.Property(r => r.LastErrorCode).HasMaxLength(100);
        b.Property(r => r.LeaseToken).IsConcurrencyToken();
        b.HasIndex(r => new { r.Provider, r.ProviderRefundId }).IsUnique().HasFilter("\"ProviderRefundId\" IS NOT NULL");
        b.HasIndex(r => new { r.NextAttemptAt, r.Id }).HasFilter("\"Status\" = 'Pending'").HasDatabaseName("IX_Refunds_Due");
        foreach (var name in new[] { nameof(PaymentRefund.CaptureId), nameof(PaymentRefund.Provider), nameof(PaymentRefund.ProviderCaptureId),
            nameof(PaymentRefund.Purpose), nameof(PaymentRefund.ProviderRefundKey), nameof(PaymentRefund.Amount), nameof(PaymentRefund.Currency),
            nameof(PaymentRefund.Reason), nameof(PaymentRefund.CreatedAt) })
            b.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
