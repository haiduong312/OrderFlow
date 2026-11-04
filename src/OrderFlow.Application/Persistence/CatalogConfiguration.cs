using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Modules.Catalog;

namespace OrderFlow.Application.Persistence;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", "catalog", table =>
        {
            table.HasCheckConstraint("CK_Categories_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Categories_Version", "\"Version\" > 0");
            table.HasCheckConstraint("CK_Categories_CreatedAt", "isfinite(\"CreatedAt\")");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Name).IsRequired().HasMaxLength(200);
        builder.Property(row => row.Version).IsConcurrencyToken();
        builder.HasIndex(row => new { row.CreatedAt, row.Id }).IsDescending(true, true).HasDatabaseName("IX_Categories_CreatedAt_Id");
    }
}
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", "catalog", table =>
        {
            table.HasCheckConstraint("CK_Products_Sku", "\"Sku\" ~ '^[A-Z0-9-]{1,64}$'");
            table.HasCheckConstraint("CK_Products_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Products_Price", "\"Price\" > 0 AND \"Price\" <= 9999999999999999.99");
            table.HasCheckConstraint("CK_Products_Version", "\"Version\" > 0");
            table.HasCheckConstraint("CK_Products_CreatedAt", "isfinite(\"CreatedAt\")");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Sku).IsRequired().HasMaxLength(64).UseCollation("C")
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(row => row.Name).IsRequired().HasMaxLength(200);
        builder.Property(row => row.Description).IsRequired().HasMaxLength(2000);
        builder.Property(row => row.Price).HasPrecision(18, 2);
        builder.Property(row => row.Version).IsConcurrencyToken();
        builder.HasIndex(row => row.Sku).IsUnique().HasDatabaseName("UX_Products_Sku");
        builder.HasIndex(row => new { row.CreatedAt, row.Id }).IsDescending(true, true).HasDatabaseName("IX_Products_CreatedAt_Id");
        builder.HasIndex(row => new { row.CategoryId, row.CreatedAt, row.Id }).IsDescending(false, true, true)
            .HasDatabaseName("IX_Products_Category_CreatedAt_Id");
        builder.HasOne(row => row.Category).WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
