using AIFramework.Core.Models;

namespace AIFramework.Core.Metering;

internal static class AiMeteringIdentifier
{
    internal static void Validate(string value, int maximum, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum ||
            value.Any(c => !((c is >= 'A' and <= 'Z') || (c is >= 'a' and <= 'z') ||
                             (c is >= '0' and <= '9') || c is '.' or '_' or ':' or '-')))
            throw new ArgumentException($"Invalid {name}.", name);
    }
}

public sealed record AiUsageScope(string Product, string Environment, string TenantKey, string Feature)
{
    public void Validate()
    {
        AiMeteringIdentifier.Validate(Product, 100, nameof(Product));
        AiMeteringIdentifier.Validate(Environment, 100, nameof(Environment));
        AiMeteringIdentifier.Validate(TenantKey, 100, nameof(TenantKey));
        AiMeteringIdentifier.Validate(Feature, 100, nameof(Feature));
    }
}

public sealed record AiModelKey(string Provider, string Model)
{
    public void Validate()
    {
        AiMeteringIdentifier.Validate(Provider, 100, nameof(Provider));
        AiMeteringIdentifier.Validate(Model, 100, nameof(Model));
    }
}

public sealed record AiPrice(AiModelKey Key, decimal InputPerMillionTokens,
    decimal OutputPerMillionTokens, string Currency, string PriceVersion)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Key);
        Key.Validate();
        if (InputPerMillionTokens < 0 || OutputPerMillionTokens < 0 ||
            Currency is null || Currency.Length != 3 || Currency.Any(c => c is not (>= 'A' and <= 'Z')) ||
            string.IsNullOrWhiteSpace(PriceVersion))
            throw new ArgumentException("Invalid AI price.");
    }
}

public sealed class AiPriceCatalog
{
    private readonly Dictionary<string, AiPrice> prices = new(StringComparer.OrdinalIgnoreCase);

    public AiPriceCatalog(IEnumerable<AiPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);
        string? currency = null;
        foreach (var price in prices)
        {
            ArgumentNullException.ThrowIfNull(price);
            price.Validate();
            currency ??= price.Currency;
            if (!string.Equals(currency, price.Currency, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("AI prices must use one currency.", nameof(prices));
            if (!this.prices.TryAdd(Identity(price.Key), price))
                throw new ArgumentException("Duplicate AI model price.", nameof(prices));
        }
    }

    public bool TryGet(AiModelKey key, out AiPrice price)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        return prices.TryGetValue(Identity(key), out price!);
    }

    private static string Identity(AiModelKey key) => key.Provider + "\0" + key.Model;
}

public static class AiCostCalculator
{
    public static long EstimateInputTokensUpperBound(params string[] texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        long total = 0;
        foreach (string text in texts)
        {
            ArgumentNullException.ThrowIfNull(text);
            total = checked(total + System.Text.Encoding.UTF8.GetByteCount(text) + 32L);
        }
        return total;
    }

    public static decimal MaximumCost(AiPrice price, long inputTokenBound, int maxOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(price);
        price.Validate();
        if (inputTokenBound < 0 || maxOutputTokens < 0) throw new ArgumentOutOfRangeException(nameof(inputTokenBound));
        return (inputTokenBound * price.InputPerMillionTokens + maxOutputTokens * price.OutputPerMillionTokens) / 1_000_000m;
    }

    public static decimal? Cost(AiPrice price, GenerationUsage usage)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(usage);
        price.Validate();
        if (usage.InputTokens is null || usage.OutputTokens is null) return null;
        if (usage.InputTokens < 0 || usage.OutputTokens < 0) return null;
        return (usage.InputTokens.Value * price.InputPerMillionTokens +
                usage.OutputTokens.Value * price.OutputPerMillionTokens) / 1_000_000m;
    }
}

public enum AiOutcome { Completed, Incomplete, Refused, Error, Cancelled }

public sealed record AiReservationRequest(string RequestKey, AiUsageScope Scope, AiModelKey ModelKey,
    decimal Amount, string Currency, string PriceVersion)
{
    public void Validate()
    {
        AiMeteringIdentifier.Validate(RequestKey, 128, nameof(RequestKey));
        ArgumentNullException.ThrowIfNull(Scope);
        ArgumentNullException.ThrowIfNull(ModelKey);
        Scope.Validate();
        ModelKey.Validate();
        if (Amount < 0 || Currency is null || Currency.Length != 3 ||
            Currency.Any(c => c is not (>= 'A' and <= 'Z')) || string.IsNullOrWhiteSpace(PriceVersion))
            throw new ArgumentException("Invalid AI reservation request.");
    }
}

public sealed record AiReservation(string ReservationId, bool Granted, string? DenialCode);

public sealed record AiUsageRecord(string ReservationId, string RequestKey, AiUsageScope Scope,
    AiModelKey ModelKey, string? ResponseModel, int? InputTokens, int? OutputTokens,
    decimal ReservedAmount, decimal EstimatedCost, string Currency, string PriceVersion,
    AiOutcome Outcome, bool UsageReported, DateTimeOffset StartedAt, DateTimeOffset CompletedAt);

/// <summary>
/// Reservations are atomic against every applicable limit. A RequestKey never reserves twice.
/// Commit and release are idempotent. Each reservation counts in the period when it was made.
/// A durable implementation must preserve these guarantees across processes and restarts.
/// </summary>
public interface IAiBudgetLedger
{
    Task<AiReservation> ReserveAsync(AiReservationRequest request, CancellationToken cancellationToken);
    Task CommitAsync(AiUsageRecord record, CancellationToken cancellationToken);
    Task ReleaseAsync(string reservationId, AiOutcome reason, CancellationToken cancellationToken);
}

public sealed class AiMeteringException(string code) : Exception($"AI metering denied: {code}.")
{
    public string Code { get; } = code;
}
