using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Application.Persistence;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.ToTable("Notifications", "notifications", table =>
        {
            table.HasCheckConstraint("CK_Notification_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SourceEventId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Notification_Type", "\"Type\" IN ('ordering.order-created','ordering.order-confirmed','ordering.order-cancelled','ordering.order-expired','ordering.order-processing','ordering.order-shipped','ordering.order-completed')");
            table.HasCheckConstraint("CK_Notification_Times", "isfinite(\"CreatedAt\") AND (\"ReadAt\" IS NULL OR (isfinite(\"ReadAt\") AND \"ReadAt\" >= \"CreatedAt\"))");
        });
        entity.HasKey(n => n.Id); entity.Property(n => n.Id).ValueGeneratedNever();
        entity.Property(n => n.Type).HasMaxLength(200).IsRequired();
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Order>().WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(n => new { n.SourceEventId, n.Type, n.RecipientUserId }).IsUnique().HasDatabaseName("UX_Notification_Source_Type_Recipient");
        entity.HasIndex(n => new { n.RecipientUserId, n.CreatedAt, n.Id }).IsDescending(false, true, true).HasDatabaseName("IX_Notification_User_Created");
        entity.HasIndex(n => n.RecipientUserId).HasFilter("\"ReadAt\" IS NULL").HasDatabaseName("IX_Notification_User_Unread");
        foreach (var name in new[] { nameof(Notification.RecipientUserId), nameof(Notification.SourceEventId), nameof(Notification.Type), nameof(Notification.OrderId), nameof(Notification.CreatedAt) })
            entity.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
