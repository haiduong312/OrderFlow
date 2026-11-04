using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CatalogPersistenceTests(PostgresDatabase database)
{
    [Fact]
    public async Task PostgreSQL_enforces_unique_normalized_sku_positive_price_names_versions_and_foreign_keys()
    {
        var category = Category.Create("Constraints", DateTimeOffset.UnixEpoch);
        var sku = "SQL-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var product = Product.Create(sku, "Original", "", UsdMoney.FromDecimal(1m), category.Id, DateTimeOffset.UnixEpoch);
        await using (var context = database.CreateContext())
        {
            context.Add(category);
            context.Add(product);
            await context.SaveChangesAsync();
        }
        await using (var duplicate = database.CreateContext())
        {
            duplicate.Add(Product.Create(sku.ToLowerInvariant(), "Duplicate", "", UsdMoney.FromDecimal(1m), category.Id, DateTimeOffset.UnixEpoch));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }
        foreach (var (sql, constraint) in new[]
        {
            ("UPDATE catalog.\"Products\" SET \"Price\" = 0 WHERE \"Id\" = @id", "CK_Products_Price"),
            ("UPDATE catalog.\"Products\" SET \"Name\" = ' ' WHERE \"Id\" = @id", "CK_Products_Name"),
            ("UPDATE catalog.\"Products\" SET \"Sku\" = 'lower-case' WHERE \"Id\" = @id", "CK_Products_Sku"),
            ("UPDATE catalog.\"Products\" SET \"Version\" = 0 WHERE \"Id\" = @id", "CK_Products_Version"),
            ("UPDATE catalog.\"Products\" SET \"CreatedAt\" = 'infinity' WHERE \"Id\" = @id", "CK_Products_CreatedAt")
        })
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", product.Id);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal(constraint, exception.ConstraintName);
        }
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var delete = new NpgsqlCommand("DELETE FROM catalog.\"Categories\" WHERE \"Id\" = @id", connection);
            delete.Parameters.AddWithValue("id", category.Id);
            Assert.Equal(PostgresErrorCodes.RestrictViolation, (await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync())).SqlState);
            await using var missing = new NpgsqlCommand("UPDATE catalog.\"Products\" SET \"CategoryId\" = @missing WHERE \"Id\" = @id", connection);
            missing.Parameters.AddWithValue("missing", Guid.NewGuid());
            missing.Parameters.AddWithValue("id", product.Id);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => missing.ExecuteNonQueryAsync())).SqlState);
            await using var length = new NpgsqlCommand("UPDATE catalog.\"Products\" SET \"Name\" = @name WHERE \"Id\" = @id", connection);
            length.Parameters.AddWithValue("name", new string('x', 201));
            length.Parameters.AddWithValue("id", product.Id);
            Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, (await Assert.ThrowsAsync<PostgresException>(() => length.ExecuteNonQueryAsync())).SqlState);
        }
    }

    [Fact]
    public async Task Mapping_has_expected_precision_indexes_utc_time_and_no_inventory_columns()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT column_name, data_type, numeric_precision, numeric_scale
            FROM information_schema.columns WHERE table_schema='catalog' AND table_name='Products'
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
            if (reader.GetString(0) == "Price")
            {
                Assert.Equal("numeric", reader.GetString(1));
                Assert.Equal(18, reader.GetInt32(2));
                Assert.Equal(2, reader.GetInt32(3));
            }
            if (reader.GetString(0) == "CreatedAt") Assert.Equal("timestamp with time zone", reader.GetString(1));
        }
        Assert.DoesNotContain("OnHand", columns);
        Assert.DoesNotContain("Reserved", columns);
        await reader.CloseAsync();
        await using var indexes = new NpgsqlCommand("SELECT indexname FROM pg_indexes WHERE schemaname='catalog'", connection);
        await using var indexReader = await indexes.ExecuteReaderAsync();
        var names = new List<string>();
        while (await indexReader.ReadAsync()) names.Add(indexReader.GetString(0));
        Assert.Contains("UX_Products_Sku", names);
        Assert.Contains("IX_Products_Category_CreatedAt_Id", names);
        Assert.Contains("IX_Products_CreatedAt_Id", names);
        Assert.Contains("IX_Categories_CreatedAt_Id", names);
    }

    [Fact]
    public async Task Locked_category_check_reads_fresh_state_even_when_context_already_tracks_an_old_category()
    {
        var category = Category.Create("Fresh state", DateTimeOffset.UnixEpoch);
        await using var first = database.CreateContext();
        first.Add(category);
        await first.SaveChangesAsync();
        await using (var other = database.CreateContext())
        {
            var service = new CatalogService(other, TimeProvider.System);
            await service.ArchiveCategoryAsync(category.Id, new ArchiveRequest { ExpectedVersion = 1 }, default);
        }
        // First context still tracks IsActive=true; a tracking SQL query would reuse it.
        Assert.True(category.IsActive);
        var attempt = new CatalogService(first, TimeProvider.System);
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => attempt.CreateProductAsync(new CreateProductRequest
        {
            Sku = "FRESH-" + Guid.NewGuid().ToString("N"), Name = "Blocked", Price = UsdMoney.FromDecimal(1m), CategoryId = category.Id
        }, default));
        Assert.Equal(ApplicationError.StateConflict, failure.Error);
        Assert.False(await first.Products.AnyAsync(product => product.CategoryId == category.Id));
    }
}
