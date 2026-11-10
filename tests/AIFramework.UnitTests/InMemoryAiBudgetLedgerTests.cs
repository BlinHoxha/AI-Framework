using AIFramework.Core.Metering;
using AIFramework.Local;

namespace AIFramework.UnitTests;

public sealed class InMemoryAiBudgetLedgerTests
{
    [Fact]
    public async Task Concurrent_reservations_never_exceed_product_limit()
    {
        var ledger = Ledger(new AiBudgetLimit("app", "prod", null, 10m, "USD"));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => ledger.ReserveAsync(Request($"key-{i}", "a", 1m), CancellationToken.None))));
        var granted = attempts.Where(r => r.Granted).ToArray();
        Assert.Equal(10, granted.Length);
        Assert.All(attempts.Where(r => !r.Granted), r => Assert.Equal("budget_exhausted", r.DenialCode));
        foreach (var reservation in granted.Take(5))
            await ledger.CommitAsync(Record(reservation.ReservationId, $"key-{Array.IndexOf(attempts, reservation)}", "a", 1m, 1m), CancellationToken.None);
        Assert.False((await ledger.ReserveAsync(Request("extra", "a", 1m), CancellationToken.None)).Granted);
    }

    [Fact]
    public async Task Tenant_and_product_limits_are_both_enforced()
    {
        var ledger = Ledger(new AiBudgetLimit("app", "prod", null, 5m, "USD"),
            new AiBudgetLimit("app", "prod", "a", 2m, "USD"), new AiBudgetLimit("app", "prod", "b", 2m, "USD"));
        Assert.True((await ledger.ReserveAsync(Request("a1", "a", 2m), CancellationToken.None)).Granted);
        Assert.Equal("budget_exhausted", (await ledger.ReserveAsync(Request("a2", "a", 1m), CancellationToken.None)).DenialCode);
        Assert.True((await ledger.ReserveAsync(Request("b1", "b", 2m), CancellationToken.None)).Granted);
        Assert.True((await ledger.ReserveAsync(Request("c1", "c", 1m), CancellationToken.None)).Granted);
        Assert.Equal("budget_exhausted", (await ledger.ReserveAsync(Request("c2", "c", 1m), CancellationToken.None)).DenialCode);
    }

    [Fact]
    public async Task Missing_budget_and_duplicate_key_are_denied()
    {
        var ledger = Ledger();
        Assert.Equal("no_budget", (await ledger.ReserveAsync(Request("key", "a", 1m), CancellationToken.None)).DenialCode);
        ledger = Ledger(new AiBudgetLimit("app", "prod", null, 10m, "USD"));
        Assert.True((await ledger.ReserveAsync(Request("key", "a", 1m), CancellationToken.None)).Granted);
        Assert.Equal("duplicate_request", (await ledger.ReserveAsync(Request("key", "b", 1m), CancellationToken.None)).DenialCode);
    }

    [Fact]
    public async Task Commit_replaces_reservation_release_frees_it_and_missing_usage_charges_reserved()
    {
        var ledger = Ledger(new AiBudgetLimit("app", "prod", null, 5m, "USD"));
        var first = await ledger.ReserveAsync(Request("first", "a", 4m), CancellationToken.None);
        await ledger.CommitAsync(Record(first.ReservationId, "first", "a", 4m, 1m), CancellationToken.None);
        await ledger.CommitAsync(Record(first.ReservationId, "first", "a", 4m, 1m), CancellationToken.None);
        var second = await ledger.ReserveAsync(Request("second", "a", 4m), CancellationToken.None);
        Assert.True(second.Granted);
        await ledger.ReleaseAsync(second.ReservationId, AiOutcome.Cancelled, CancellationToken.None);
        await ledger.ReleaseAsync(second.ReservationId, AiOutcome.Cancelled, CancellationToken.None);
        var third = await ledger.ReserveAsync(Request("third", "a", 4m), CancellationToken.None);
        Assert.True(third.Granted);
        await ledger.CommitAsync(Record(third.ReservationId, "third", "a", 4m, 4m, false), CancellationToken.None);
        Assert.Equal("budget_exhausted", (await ledger.ReserveAsync(Request("fourth", "a", 1m), CancellationToken.None)).DenialCode);
    }

    [Fact]
    public async Task Reservation_stays_in_its_original_utc_month_after_commit()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 31, 23, 59, 0, TimeSpan.Zero));
        var ledger = new InMemoryAiBudgetLedger([new("app", "prod", null, 10m, "USD")], clock);
        var january = await ledger.ReserveAsync(Request("january", "a", 8m), CancellationToken.None);
        clock.Now = new DateTimeOffset(2026, 2, 1, 0, 1, 0, TimeSpan.Zero);
        await ledger.CommitAsync(Record(january.ReservationId, "january", "a", 8m, 8m), CancellationToken.None);
        Assert.True((await ledger.ReserveAsync(Request("february", "a", 8m), CancellationToken.None)).Granted);
        Assert.Equal("budget_exhausted", (await ledger.ReserveAsync(Request("february-2", "a", 3m), CancellationToken.None)).DenialCode);
    }

    private static InMemoryAiBudgetLedger Ledger(params AiBudgetLimit[] limits)
        => new(limits, TimeProvider.System);

    private static AiReservationRequest Request(string key, string tenant, decimal amount)
        => new(key, new("app", "prod", tenant, "feature"), new("provider", "model"), amount, "USD", "v1");

    private static AiUsageRecord Record(string id, string key, string tenant, decimal reserved,
        decimal cost, bool reported = true)
        => new(id, key, new("app", "prod", tenant, "feature"), new("provider", "model"),
            "model", reported ? 1 : null, reported ? 1 : null, reserved, cost, "USD", "v1",
            AiOutcome.Completed, reported, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
