using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;
using Ta3lom.Store.Domain.Catalog;
using Ta3lom.Store.Domain.Payments;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// Every enum property stores exactly the values its CHECK list allows (database/01 §2: text + CHECK).
public sealed partial class EnumMappingTests
{
    [Fact]
    public void Each_enum_column_maps_to_exactly_its_CHECK_values()
    {
        using var db = StoreSchema.CreateContext("Host=localhost;Database=unused");
        var mismatches = new List<string>();
        var checkedColumns = 0;

        var model = db.GetService<IDesignTimeModel>().Model;   // check constraints live in the design-time model
        foreach (var entity in model.GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            var table = entity.GetTableName()!;
            foreach (var property in entity.GetProperties())
            {
                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!enumType.IsEnum)
                {
                    continue;
                }

                var column = property.GetColumnName();
                var check = entity.GetCheckConstraints().Single(c => c.Name == $"{table}_{column}_check");
                var allowed = CheckValue().Matches(check.Sql).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList();
                var converter = property.GetValueConverter()!;
                var wire = Enum.GetValues(enumType).Cast<object>().Select(v => (string)converter.ConvertToProvider(v)!).Order(StringComparer.Ordinal).ToList();
                if (!allowed.SequenceEqual(wire))
                {
                    mismatches.Add($"{table}.{column}: CHECK [{string.Join(",", allowed)}] vs enum [{string.Join(",", wire)}]");
                }

                checkedColumns++;
            }
        }

        mismatches.ShouldBeEmpty();
        checkedColumns.ShouldBe(39);
    }

    [Fact]
    public void Wire_values_that_snake_case_cannot_produce_are_explicit()
    {
        using var db = StoreSchema.CreateContext("Host=localhost;Database=unused");
        var method = db.Model.FindEntityType(typeof(Payment))!.FindProperty(nameof(Payment.Method))!.GetValueConverter()!;

        method.ConvertToProvider(PaymentMethod.ApplePay).ShouldBe("applepay");
        method.ConvertToProvider(PaymentMethod.StcPay).ShouldBe("stcpay");
    }

    [Fact]
    public async Task Seeded_catalogue_reads_back_through_EF()
    {
        await using var db = StoreSchema.CreateContext(SharedStoreDatabase.ConnectionString);

        var products = await db.Set<Product>().ToListAsync(TestContext.Current.CancellationToken);

        products.Count.ShouldBe(15);
        products.GroupBy(p => p.Type).ToDictionary(g => g.Key, g => g.Count())
            .ShouldBe(new Dictionary<ProductType, int> { [ProductType.Course] = 6, [ProductType.Book] = 4, [ProductType.Cause] = 5 }, ignoreOrder: true);
        products.Single(p => p.Slug == "digital-teacher").Badge.ShouldBe(ProductBadge.Best);
        var offer = await db.Set<SponsorshipOffer>().FirstAsync(o => o.IsActive, TestContext.Current.CancellationToken);
        offer.SeatPrice.IsPositive.ShouldBeTrue();
    }

    [GeneratedRegex(@"'([^']*)'::text")]
    private static partial Regex CheckValue();
}
