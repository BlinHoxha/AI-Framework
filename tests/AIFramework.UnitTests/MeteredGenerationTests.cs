using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using AIFramework.Application.Metering;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Metering;
using AIFramework.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AIFramework.UnitTests;

public sealed class MeteredGenerationTests
{
    private static readonly AiUsageScope Scope = new("product", "test", "secret-tenant", "feature");
    private static readonly AiModelKey Key = new("provider", "model");
    private static readonly TextGenerationRequest TextRequest = new("secret-prompt", "secret-input", 10);
    private static readonly AiPrice Price = new(Key, 1_000_000m, 2_000_000m, "USD", "v1");

    [Fact]
    public async Task Missing_price_denies_before_reservation_or_provider()
    {
        var ledger = new StubLedger();
        var model = new TextModel();
        var service = TextService(model, ledger, new AiPriceCatalog([]));
        var error = await Assert.ThrowsAsync<AiMeteringException>(() =>
            service.GenerateTextAsync(Scope, "key", TextRequest, CancellationToken.None));
        Assert.Equal("no_price", error.Code);
        Assert.Null(ledger.Reservation);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("no_budget")]
    [InlineData("budget_exhausted")]
    [InlineData("duplicate_request")]
    public async Task Ledger_denial_prevents_provider_call(string code)
    {
        var ledger = new StubLedger { DenialCode = code };
        var model = new TextModel();
        var error = await Assert.ThrowsAsync<AiMeteringException>(() =>
            TextService(model, ledger).GenerateTextAsync(Scope, "key", TextRequest, CancellationToken.None));
        Assert.Equal(code, error.Code);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Success_commits_exact_estimate_and_incomplete_outcome()
    {
        var ledger = new StubLedger();
        var model = new TextModel { Result = new("text", "provider")
            { Model = "underlying", Usage = new(3, 2), Complete = false } };
        var result = await TextService(model, ledger).GenerateTextAsync(Scope, "key", TextRequest, CancellationToken.None);
        Assert.Same(model.Result, result);
        Assert.Equal(AiOutcome.Incomplete, ledger.Committed!.Outcome);
        Assert.Equal("underlying", ledger.Committed.ResponseModel);
        Assert.Equal(7m, ledger.Committed.EstimatedCost);
        Assert.True(ledger.Committed.UsageReported);
        Assert.True(ledger.Reservation!.Amount >= ledger.Committed.EstimatedCost);
        Assert.Equal(CancellationToken.None, ledger.CommitToken);
    }

    [Fact]
    public async Task Missing_usage_charges_reserved_amount()
    {
        var ledger = new StubLedger();
        await TextService(new TextModel(), ledger).GenerateTextAsync(Scope, "key", TextRequest, CancellationToken.None);
        Assert.Equal(ledger.Reservation!.Amount, ledger.Committed!.EstimatedCost);
        Assert.False(ledger.Committed.UsageReported);
        Assert.Equal(AiOutcome.Completed, ledger.Committed.Outcome);
    }

    [Fact]
    public async Task Inner_exception_commits_reserved_amount_and_rethrows_same_instance()
    {
        var error = new InvalidOperationException("provider failed");
        var ledger = new StubLedger();
        var model = new TextModel { Error = error };
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TextService(model, ledger).GenerateTextAsync(Scope, "key", TextRequest, CancellationToken.None));
        Assert.Same(error, caught);
        Assert.Equal(AiOutcome.Error, ledger.Committed!.Outcome);
        Assert.Equal(ledger.Reservation!.Amount, ledger.Committed.EstimatedCost);
    }

    [Fact]
    public async Task Pre_cancelled_token_releases_without_calling_provider()
    {
        var ledger = new StubLedger();
        var model = new TextModel();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TextService(model, ledger).GenerateTextAsync(Scope, "key", TextRequest, cancellation.Token));
        Assert.Equal(0, model.Calls);
        Assert.Equal(AiOutcome.Cancelled, ledger.Released);
        Assert.Equal(CancellationToken.None, ledger.ReleaseToken);
        Assert.Null(ledger.Committed);
    }

    [Fact]
    public async Task Cancellation_during_provider_call_commits_cancelled()
    {
        var ledger = new StubLedger();
        using var cancellation = new CancellationTokenSource();
        var model = new TextModel { OnCall = () => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TextService(model, ledger).GenerateTextAsync(Scope, "key", TextRequest, cancellation.Token));
        Assert.Equal(AiOutcome.Cancelled, ledger.Committed!.Outcome);
        Assert.Equal(ledger.Reservation!.Amount, ledger.Committed.EstimatedCost);
    }

    [Theory]
    [InlineData(StructuredGenerationStatus.Refused, AiOutcome.Refused)]
    [InlineData(StructuredGenerationStatus.Incomplete, AiOutcome.Incomplete)]
    public async Task Structured_status_is_committed(StructuredGenerationStatus status, AiOutcome expected)
    {
        var ledger = new StubLedger();
        var model = new StructuredModel { Result = new(status, null, "provider", new(2, 1)) { Model = "underlying" } };
        var service = new MeteredStructuredGeneration(model, ledger, Catalog(),
            new AiMeteringOptions { StructuredModel = Key }, TimeProvider.System);
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        var result = await service.GenerateStructuredAsync(Scope, "key",
            new("schema", "instructions", "input", schema.RootElement, 10), CancellationToken.None);
        Assert.Same(model.Result, result);
        Assert.Equal(expected, ledger.Committed!.Outcome);
        Assert.Equal("underlying", ledger.Committed.ResponseModel);
        Assert.Equal(4m, ledger.Committed.EstimatedCost);
        Assert.True(ledger.Reservation!.Amount >= 4m);
    }

    [Fact]
    public async Task Telemetry_tags_exclude_prompts_inputs_tenants_and_request_keys()
    {
        var observed = new List<KeyValuePair<string, object?>>();
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "AIFramework.Metering",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => observed.AddRange(activity.TagObjects)
        };
        ActivitySource.AddActivityListener(activities);
        using var metrics = new MeterListener();
        metrics.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "AIFramework.Metering") listener.EnableMeasurementEvents(instrument);
        };
        metrics.SetMeasurementEventCallback<long>((_, _, tags, _) => observed.AddRange(tags.ToArray()));
        metrics.SetMeasurementEventCallback<decimal>((_, _, tags, _) => observed.AddRange(tags.ToArray()));
        metrics.Start();
        await TextService(new TextModel { Result = new("secret-output", "provider") { Usage = new(1, 1) } },
            new StubLedger()).GenerateTextAsync(Scope, "secret-request-key", TextRequest, CancellationToken.None);
        Assert.NotEmpty(observed);
        string[] allowed = ["product", "environment", "feature", "provider", "model", "outcome", "denial_code"];
        Assert.All(observed, tag =>
        {
            Assert.Contains(tag.Key, allowed);
            Assert.DoesNotContain(tag.Value?.ToString(), new[] { "secret-prompt", "secret-input", "secret-output", "secret-tenant", "secret-request-key" });
        });
    }

    [Fact]
    public void Registration_requires_a_ledger_at_service_resolution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITextGenerationModel>(new TextModel());
        services.AddAiMetering(Catalog(), options => options.TextModel = Key);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<MeteredTextGeneration>());
    }

    private static AiPriceCatalog Catalog() => new([Price]);
    private static MeteredTextGeneration TextService(TextModel model, StubLedger ledger, AiPriceCatalog? catalog = null)
        => new(model, ledger, catalog ?? Catalog(), new AiMeteringOptions { TextModel = Key }, TimeProvider.System);

    private sealed class TextModel : ITextGenerationModel
    {
        public int Calls { get; private set; }
        public TextGenerationResult Result { get; set; } = new("text", "provider");
        public Exception? Error { get; set; }
        public Action? OnCall { get; set; }
        public Task<TextGenerationResult> GenerateTextAsync(TextGenerationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            OnCall?.Invoke();
            if (Error is not null) throw Error;
            return Task.FromResult(Result);
        }
    }

    private sealed class StructuredModel : IStructuredGenerationModel
    {
        public StructuredGenerationResult Result { get; set; } = new(StructuredGenerationStatus.Completed, "{}", "provider", new(1, 1));
        public Task<StructuredGenerationResult> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result);
    }

    private sealed class StubLedger : IAiBudgetLedger
    {
        public string? DenialCode { get; set; }
        public AiReservationRequest? Reservation { get; private set; }
        public AiUsageRecord? Committed { get; private set; }
        public AiOutcome? Released { get; private set; }
        public CancellationToken CommitToken { get; private set; }
        public CancellationToken ReleaseToken { get; private set; }
        public Task<AiReservation> ReserveAsync(AiReservationRequest request, CancellationToken cancellationToken)
        {
            Reservation = request;
            return Task.FromResult(DenialCode is null ? new AiReservation("reservation", true, null) :
                new AiReservation("", false, DenialCode));
        }
        public Task CommitAsync(AiUsageRecord record, CancellationToken cancellationToken)
        {
            Committed = record; CommitToken = cancellationToken; return Task.CompletedTask;
        }
        public Task ReleaseAsync(string reservationId, AiOutcome reason, CancellationToken cancellationToken)
        {
            Released = reason; ReleaseToken = cancellationToken; return Task.CompletedTask;
        }
    }
}
