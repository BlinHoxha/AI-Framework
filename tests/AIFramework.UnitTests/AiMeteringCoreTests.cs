using AIFramework.Core.Metering;
using AIFramework.Core.Models;

namespace AIFramework.UnitTests;

public sealed class AiMeteringCoreTests
{
    [Theory]
    [InlineData("")]
    [InlineData("bad key")]
    [InlineData("bad/key")]
    public void Scope_and_model_reject_invalid_identifiers(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new AiUsageScope(invalid, "prod", "tenant", "feature").Validate());
        Assert.Throws<ArgumentException>(() => new AiModelKey("provider", invalid).Validate());
        Assert.Throws<ArgumentException>(() => Reservation(invalid).Validate());
    }

    [Fact]
    public void Identifier_boundaries_are_enforced()
    {
        new AiUsageScope(new string('p', 100), "prod", "tenant", "feature").Validate();
        new AiModelKey("provider", new string('m', 100)).Validate();
        Reservation(new string('k', 128)).Validate();
        Assert.Throws<ArgumentException>(() => new AiUsageScope(new string('p', 101), "prod", "tenant", "feature").Validate());
        Assert.Throws<ArgumentException>(() => new AiModelKey("provider", new string('m', 101)).Validate());
        Assert.Throws<ArgumentException>(() => Reservation(new string('k', 129)).Validate());
    }

    [Fact]
    public void Catalog_rejects_duplicates_and_mixed_currency_and_looks_up_case_insensitively()
    {
        var one = Price("Azure", "GPT", "USD");
        var catalog = new AiPriceCatalog([one]);
        Assert.True(catalog.TryGet(new("azure", "gpt"), out var found));
        Assert.Same(one, found);
        Assert.False(catalog.TryGet(new("azure", "other"), out _));
        Assert.Throws<ArgumentException>(() => new AiPriceCatalog([one, Price("AZURE", "gpt", "USD")]));
        Assert.Throws<ArgumentException>(() => new AiPriceCatalog([one, Price("other", "model", "EUR")]));
        Assert.Throws<ArgumentException>(() =>
            (Price("other", "model", "USD") with { InputPerMillionTokens = -1m }).Validate());
    }

    [Fact]
    public void Calculator_uses_utf8_bytes_and_exact_decimal_costs()
    {
        Assert.Equal(67, AiCostCalculator.EstimateInputTokensUpperBound("é", "x"));
        var price = new AiPrice(new("p", "m"), 1.5m, 2.5m, "USD", "v1");
        Assert.Equal(0.000105m, AiCostCalculator.MaximumCost(price, 50, 12));
        Assert.Equal(0.0000225m, AiCostCalculator.Cost(price, new GenerationUsage(10, 3)));
        Assert.Null(AiCostCalculator.Cost(price, new GenerationUsage(10, null)));
    }

    private static AiReservationRequest Reservation(string key) => new(key,
        new("product", "production", "tenant", "feature"), new("provider", "model"), 1m, "USD", "v1");
    private static AiPrice Price(string provider, string model, string currency)
        => new(new(provider, model), 1m, 2m, currency, "v1");
}
