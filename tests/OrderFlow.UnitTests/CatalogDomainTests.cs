using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class CatalogDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Products_normalize_sku_and_preserve_identity_across_edits_and_archival()
    {
        var categoryId = Guid.NewGuid();
        var product = Product.Create("  ab-123  ", "  Original  ", " details ", UsdMoney.FromDecimal(12.50m), categoryId, Now);
        Assert.Equal("AB-123", product.Sku);
        Assert.Equal("Original", product.Name);
        Assert.Equal("details", product.Description);
        Assert.True(product.IsActive);
        Assert.Equal(1, product.Version);
        var id = product.Id;
        product.Update("Changed", null, UsdMoney.FromDecimal(15m), categoryId, true, 1);
        Assert.Equal(2, product.Version);
        Assert.Equal("AB-123", product.Sku);
        product.Archive(2);
        Assert.False(product.IsActive);
        Assert.Equal(3, product.Version);
        product.Archive(3);
        Assert.Equal(3, product.Version);
        Assert.Equal(id, product.Id);
        Assert.Equal(Now, product.CreatedAt);
        var stale = Assert.Throws<ApplicationFailureException>(() => product.Archive(2));
        Assert.Equal(ApplicationError.VersionConflict, stale.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("sku space")]
    [InlineData("é")]
    [InlineData("ı")]
    [InlineData("x_")]
    public void Invalid_skus_are_rejected_before_normalization(string sku) =>
        AssertValidation(() => Product.Create(sku, "Name", null, UsdMoney.FromDecimal(1m), Guid.NewGuid(), Now));

    [Fact]
    public void Names_prices_category_and_lengths_are_domain_rules_not_only_http_annotations()
    {
        AssertValidation(() => Category.Create(" \t ", Now));
        AssertValidation(() => Category.Create(new string('a', 201), Now));
        AssertValidation(() => Product.Create(new string('a', 65), "Name", null, UsdMoney.FromDecimal(1m), Guid.NewGuid(), Now));
        AssertValidation(() => Product.Create("SKU", " ", null, UsdMoney.FromDecimal(1m), Guid.NewGuid(), Now));
        AssertValidation(() => Product.Create("SKU", "Name", new string('a', 2001), UsdMoney.FromDecimal(1m), Guid.NewGuid(), Now));
        AssertValidation(() => Product.Create("SKU", "Name", null, null, Guid.NewGuid(), Now));
        AssertValidation(() => Product.Create("SKU", "Name", null, default(UsdMoney), Guid.NewGuid(), Now));
        AssertValidation(() => Product.Create("SKU", "Name", null, UsdMoney.FromDecimal(1m), Guid.Empty, Now));
        var maximum = Product.Create("MAX", new string('a', 200), new string('b', 2000), UsdMoney.FromDecimal(UsdMoney.Maximum), Guid.NewGuid(), Now);
        Assert.Equal(UsdMoney.Maximum, maximum.Price.Value);
    }

    [Fact]
    public void Category_changes_require_version_and_failed_validation_does_not_partially_mutate()
    {
        var category = Category.Create("  Name  ", Now);
        Assert.Equal("Name", category.Name);
        AssertValidation(() => category.Update(" ", false, 1));
        Assert.True(category.IsActive);
        Assert.Equal(1, category.Version);
        category.Archive(1);
        category.Archive(2);
        Assert.Equal(2, category.Version);
        category.Update("Restored", true, 2);
        Assert.True(category.IsActive);
        Assert.Equal(3, category.Version);
        Assert.Equal(ApplicationError.VersionConflict, Assert.Throws<ApplicationFailureException>(() => category.Update("Name", true, 1)).Error);
        AssertValidation(() => category.Archive(0));
    }

    private static void AssertValidation(Action action) =>
        Assert.Equal(ApplicationError.ValidationFailed, Assert.Throws<ApplicationFailureException>(action).Error);
}
