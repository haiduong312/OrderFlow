using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Payments.Simulation;

namespace OrderFlow.Application.Persistence;

public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> b)
    {
        b.ToTable("PaymentAttempts", "payments", t =>
        {
            t.HasCheckConstraint("CK_PaymentAttempts_State", "\"Status\" IN ('Queued','Pending','Unknown','Failed') AND \"DeliveryCount\" BETWEEN 0 AND 8 AND (\"ObservedState\" IS NULL OR \"ObservedState\" IN ('Pending','Succeeded','Failed'))");
            t.HasCheckConstraint("CK_PaymentAttempts_Money", "\"Currency\" = 'USD' AND \"Amount\" > 0 AND \"Amount\" <= 9999999999999999.99");
            t.HasCheckConstraint("CK_PaymentAttempts_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND isfinite(\"NextAttemptAt\") AND \"UpdatedAt\" >= \"CreatedAt\" AND \"NextAttemptAt\" >= \"CreatedAt\"");
            t.HasCheckConstraint("CK_PaymentAttempts_Lease", "(\"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL) OR (\"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\" > \"UpdatedAt\" AND \"DeliveryCount\" > 0)");
            t.HasCheckConstraint("CK_PaymentAttempts_Observation", "(\"LastObservation\" IS NULL OR jsonb_typeof(\"LastObservation\") = 'object') AND (\"ObservedState\" IS DISTINCT FROM 'Succeeded' OR (\"Status\" = 'Pending' AND \"NeedsAttention\" AND \"ProviderTransactionId\" IS NOT NULL AND \"ProviderCaptureId\" IS NOT NULL))");
            t.HasCheckConstraint("CK_PaymentAttempts_Identity", "\"Provider\" = 'Simulator' AND \"ProviderRequestKey\" = 'payment-' || replace(\"Id\"::text,'-','')");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.ObservedState).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Provider).HasMaxLength(32); b.Property(x => x.ProviderRequestKey).HasMaxLength(80).UseCollation("C");
        b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.LastErrorCode).HasMaxLength(100);
        b.Property(x => x.ProviderTransactionId).HasMaxLength(100); b.Property(x => x.ProviderCaptureId).HasMaxLength(100);
        b.Property(x => x.LastObservation).HasColumnType("jsonb");
        b.HasIndex(x => new { x.Provider, x.ProviderRequestKey }).IsUnique();
        b.HasIndex(x => x.OrderId).IsUnique().HasFilter("\"Status\" IN ('Queued','Pending','Unknown')").HasDatabaseName("UX_PaymentAttempts_ActiveOrder");
        b.HasIndex(x => new { x.NextAttemptAt, x.Id }).HasFilter("\"Status\" IN ('Queued','Pending','Unknown') AND NOT \"NeedsAttention\"");
        b.Property(x => x.LeaseToken).IsConcurrencyToken();
        foreach (var name in new[] { nameof(PaymentAttempt.OrderId), nameof(PaymentAttempt.Provider), nameof(PaymentAttempt.ProviderRequestKey), nameof(PaymentAttempt.Amount), nameof(PaymentAttempt.Currency), nameof(PaymentAttempt.CreatedAt) })
            b.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ProviderPaymentConfiguration : IEntityTypeConfiguration<ProviderPayment>
{
    public void Configure(EntityTypeBuilder<ProviderPayment> b)
    {
        b.ToTable("ProviderPayments", "simulator", t =>
        {
            t.HasCheckConstraint("CK_ProviderPayments_Money", "\"Currency\" = 'USD' AND \"Amount\" > 0 AND \"Amount\" <= 9999999999999999.99 AND \"RequestedAmount\" > 0 AND \"RequestedAmount\" <= 9999999999999999.99");
            t.HasCheckConstraint("CK_ProviderPayments_State", "(\"State\" = 'Succeeded' AND \"CaptureId\" IS NOT NULL) OR (\"State\" IN ('Pending','Failed') AND \"CaptureId\" IS NULL)");
            t.HasCheckConstraint("CK_ProviderPayments_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND \"UpdatedAt\" >= \"CreatedAt\"");
        });
        b.HasKey(x => x.RequestKey); b.Property(x => x.RequestKey).HasMaxLength(80).UseCollation("C");
        b.Property(x => x.TransactionId).HasMaxLength(100); b.HasIndex(x => x.TransactionId).IsUnique();
        b.Property(x => x.CaptureId).HasMaxLength(100); b.HasIndex(x => x.CaptureId).IsUnique();
        b.HasIndex(x => x.AttemptId).IsUnique(); b.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Currency).HasMaxLength(3);
    }
}

public sealed class ProviderScenarioConfiguration : IEntityTypeConfiguration<ProviderScenario>
{
    public void Configure(EntityTypeBuilder<ProviderScenario> b)
    {
        b.ToTable("ProviderScenarios", "simulator"); b.HasKey(x => x.RequestKey);
        b.Property(x => x.RequestKey).HasMaxLength(80).UseCollation("C"); b.Property(x => x.Scenario).HasConversion<string>().HasMaxLength(32);
    }
}

public sealed class ProviderRefundConfiguration : IEntityTypeConfiguration<ProviderRefund>
{
    public void Configure(EntityTypeBuilder<ProviderRefund> b)
    {
        b.ToTable("ProviderRefunds", "simulator", t =>
        {
            t.HasCheckConstraint("CK_ProviderRefunds_Money", "\"Currency\" = 'USD' AND \"Amount\" > 0 AND \"Amount\" <= 9999999999999999.99 AND isfinite(\"CreatedAt\")");
        });
        b.HasKey(x => x.RequestKey); b.Property(x => x.RequestKey).HasMaxLength(80).UseCollation("C");
        b.Property(x => x.RefundId).HasMaxLength(100); b.HasIndex(x => x.RefundId).IsUnique();
        b.Property(x => x.CaptureId).HasMaxLength(100); b.HasIndex(x => x.CaptureId).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.PaymentRequestKey).HasMaxLength(80).UseCollation("C");
        b.HasOne<ProviderPayment>().WithMany().HasForeignKey(x => x.PaymentRequestKey).OnDelete(DeleteBehavior.Restrict);
    }
}
