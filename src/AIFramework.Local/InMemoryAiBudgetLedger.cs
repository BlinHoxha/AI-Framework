using AIFramework.Core.Metering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIFramework.Local;

/// <summary>A process-local monthly limit. State resets on restart; this is not a production ledger.</summary>
public sealed record AiBudgetLimit(string Product, string Environment, string? TenantKey,
    decimal MonthlyAmount, string Currency)
{
    public void Validate()
    {
        new AiUsageScope(Product, Environment, TenantKey ?? "placeholder", "placeholder").Validate();
        if (MonthlyAmount < 0 || Currency is null || Currency.Length != 3 ||
            Currency.Any(c => c is not (>= 'A' and <= 'Z')))
            throw new ArgumentException("Invalid AI budget limit.");
    }
}

/// <summary>Thread-safe development ledger. Its state is process-local and resets on restart.</summary>
public sealed class InMemoryAiBudgetLedger : IAiBudgetLedger
{
    private sealed class Entry(AiReservationRequest request, int period)
    {
        public AiReservationRequest Request { get; } = request;
        public int Period { get; } = period;
        public decimal Amount { get; set; } = request.Amount;
        public bool Open { get; set; } = true;
        public bool Released { get; set; }
    }

    private readonly object gate = new();
    private readonly AiBudgetLimit[] limits;
    private readonly TimeProvider clock;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> requestKeys = new(StringComparer.Ordinal);

    public InMemoryAiBudgetLedger(IEnumerable<AiBudgetLimit> limits, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(limits);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.limits = limits.ToArray();
        foreach (var limit in this.limits) limit.Validate();
        if (this.limits.GroupBy(l => (l.Product, l.Environment, l.TenantKey))
            .Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate AI budget limit.", nameof(limits));
    }

    public Task<AiReservation> ReserveAsync(AiReservationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        lock (gate)
        {
            if (requestKeys.Contains(request.RequestKey))
                return Task.FromResult(new AiReservation("", false, "duplicate_request"));
            var applicable = limits.Where(l => Matches(l, request.Scope)).ToArray();
            if (!applicable.Any(l => l.TenantKey is null && l.Currency == request.Currency))
                return Task.FromResult(new AiReservation("", false, "no_budget"));
            if (applicable.Any(l => l.Currency != request.Currency))
                return Task.FromResult(new AiReservation("", false, "no_budget"));
            int period = Period(clock.GetUtcNow());
            if (applicable.Any(limit => entries.Values
                .Where(entry => !entry.Released && entry.Period == period && Matches(limit, entry.Request.Scope))
                .Sum(entry => entry.Amount) + request.Amount > limit.MonthlyAmount))
                return Task.FromResult(new AiReservation("", false, "budget_exhausted"));
            string id = Guid.NewGuid().ToString("N");
            entries.Add(id, new Entry(request, period));
            requestKeys.Add(request.RequestKey);
            return Task.FromResult(new AiReservation(id, true, null));
        }
    }

    public Task CommitAsync(AiUsageRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);
        lock (gate)
        {
            if (!entries.TryGetValue(record.ReservationId, out var entry))
                throw new ArgumentException("Unknown reservation.", nameof(record));
            if (!entry.Open) return Task.CompletedTask;
            if (record.EstimatedCost < 0 || record.ReservedAmount != entry.Request.Amount ||
                record.RequestKey != entry.Request.RequestKey || record.Scope != entry.Request.Scope ||
                record.ModelKey != entry.Request.ModelKey || record.Currency != entry.Request.Currency ||
                record.PriceVersion != entry.Request.PriceVersion)
                throw new ArgumentException("Usage record does not match the reservation.", nameof(record));
            entry.Amount = record.EstimatedCost;
            entry.Open = false;
            return Task.CompletedTask;
        }
    }

    public Task ReleaseAsync(string reservationId, AiOutcome reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!entries.TryGetValue(reservationId, out var entry))
                throw new ArgumentException("Unknown reservation.", nameof(reservationId));
            if (entry.Open)
            {
                entry.Released = true;
                entry.Open = false;
                entry.Amount = 0;
            }
            return Task.CompletedTask;
        }
    }

    private static bool Matches(AiBudgetLimit limit, AiUsageScope scope)
        => limit.Product == scope.Product && limit.Environment == scope.Environment &&
           (limit.TenantKey is null || limit.TenantKey == scope.TenantKey);

    private static int Period(DateTimeOffset date) => date.Year * 12 + date.Month;
}

public static class AiBudgetLedgerRegistration
{
    public static IServiceCollection AddInMemoryAiBudgetLedger(this IServiceCollection services, params AiBudgetLimit[] limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAiBudgetLedger>(provider =>
            new InMemoryAiBudgetLedger(limits, provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}
