using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Modules.Identity;

namespace OrderFlow.Application.Persistence;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> entity)
    {
        entity.ToTable("IdempotencyRecords", "messaging", table =>
        {
            table.HasCheckConstraint("CK_Idempotency_Key", "\"Key\" ~ '^[!-~]{1,128}$'");
            table.HasCheckConstraint("CK_Idempotency_Request", "length(btrim(\"Operation\")) > 0 AND length(\"CanonicalRequest\") > 0 AND octet_length(\"RequestHash\") = 32");
            table.HasCheckConstraint("CK_Idempotency_Times", "isfinite(\"CreatedAt\") AND (\"CompletedAt\" IS NULL OR (isfinite(\"CompletedAt\") AND \"CompletedAt\" >= \"CreatedAt\"))");
            table.HasCheckConstraint("CK_Idempotency_Snapshot", "(\"Status\" = 'InProgress' AND \"CompletedAt\" IS NULL AND \"ResponseStatusCode\" IS NULL AND \"ResponseContentType\" IS NULL AND \"ResponseBody\" IS NULL AND \"ResponseLocation\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompletedAt\" IS NOT NULL AND \"ResponseStatusCode\" IS NOT NULL AND \"ResponseStatusCode\" BETWEEN 200 AND 299 AND \"ResponseContentType\" IS NOT NULL AND length(btrim(\"ResponseContentType\")) > 0 AND \"ResponseBody\" IS NOT NULL)");
        });
        entity.HasKey(record => record.Id);
        entity.Property(record => record.Operation).HasMaxLength(100).UseCollation("C").IsRequired();
        entity.Property(record => record.Key).HasMaxLength(128).UseCollation("C").IsRequired();
        entity.Property(record => record.CanonicalRequest).HasColumnType("text").IsRequired();
        entity.Property(record => record.RequestHash).HasColumnType("bytea").IsRequired();
        entity.Property(record => record.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(record => record.ResponseBody).HasColumnType("bytea");
        entity.Property(record => record.ResponseContentType).HasMaxLength(100);
        entity.Property(record => record.ResponseLocation).HasMaxLength(2048);
        entity.HasIndex(record => new { record.UserId, record.Operation, record.Key })
            .IsUnique().HasDatabaseName("UX_Idempotency_User_Operation_Key");
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(record => record.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
