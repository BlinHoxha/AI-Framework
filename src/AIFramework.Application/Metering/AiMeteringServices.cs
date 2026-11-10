using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Metering;
using AIFramework.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIFramework.Application.Metering;

public sealed class AiMeteringOptions
{
    public AiModelKey? TextModel { get; set; }
    public AiModelKey? StructuredModel { get; set; }
}

internal static class AiMeteringTelemetry
{
    internal static readonly ActivitySource Activities = new("AIFramework.Metering");
    private static readonly Meter Meter = new("AIFramework.Metering");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("ai.requests");
    private static readonly Counter<long> InputTokens = Meter.CreateCounter<long>("ai.tokens.input");
    private static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>("ai.tokens.output");
    private static readonly Counter<decimal> EstimatedCost = Meter.CreateCounter<decimal>("ai.cost.estimated");

    internal static Activity? Start(AiUsageScope scope, AiModelKey? key)
    {
        var activity = Activities.StartActivity("ai.metered_generation");
        activity?.SetTag("product", scope.Product);
        activity?.SetTag("environment", scope.Environment);
        activity?.SetTag("feature", scope.Feature);
        activity?.SetTag("provider", key?.Provider);
        activity?.SetTag("model", key?.Model);
        return activity;
    }

    internal static void Finish(Activity? activity, AiUsageScope scope, AiModelKey? key,
        string outcome, string? denialCode, GenerationUsage? usage, decimal? cost)
    {
        var tags = new TagList
        {
            { "product", scope.Product }, { "environment", scope.Environment }, { "feature", scope.Feature },
            { "provider", key?.Provider }, { "model", key?.Model }, { "outcome", outcome }
        };
        activity?.SetTag("outcome", outcome);
        if (denialCode is not null)
        {
            tags.Add("denial_code", denialCode);
            activity?.SetTag("denial_code", denialCode);
        }
        Requests.Add(1, tags);
        if (usage?.InputTokens is { } input) InputTokens.Add(input, tags);
        if (usage?.OutputTokens is { } output) OutputTokens.Add(output, tags);
        if (cost is { } amount) EstimatedCost.Add(amount, tags);
    }
}

internal static class AiMeteringFlow
{
    internal static async Task<TResult> RunAsync<TResult>(AiUsageScope scope, string requestKey, AiModelKey? key,
        int maxOutputTokens, string[] inputTexts, Func<CancellationToken, Task<TResult>> generate,
        Func<TResult, GenerationUsage?> usageOf, Func<TResult, string?> responseModelOf,
        Func<TResult, AiOutcome> outcomeOf, IAiBudgetLedger ledger, AiPriceCatalog catalog,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Validate();
        new AiReservationRequest(requestKey, scope, key ?? new("placeholder", "placeholder"),
            0, "USD", "placeholder").Validate();
        using var activity = AiMeteringTelemetry.Start(scope, key);
        if (key is null || !catalog.TryGet(key, out var price))
        {
            AiMeteringTelemetry.Finish(activity, scope, key, "denied", "no_price", null, null);
            throw new AiMeteringException("no_price");
        }
        decimal maximum = AiCostCalculator.MaximumCost(price,
            AiCostCalculator.EstimateInputTokensUpperBound(inputTexts), maxOutputTokens);
        var reservationRequest = new AiReservationRequest(requestKey, scope, key, maximum,
            price.Currency, price.PriceVersion);
        reservationRequest.Validate();
        DateTimeOffset startedAt = clock.GetUtcNow();
        // Reservation must complete even if the caller cancels: only then can it be released.
        var reservation = await ledger.ReserveAsync(reservationRequest, CancellationToken.None);
        if (!reservation.Granted)
        {
            string code = reservation.DenialCode ?? "no_budget";
            AiMeteringTelemetry.Finish(activity, scope, key, "denied", code, null, null);
            throw new AiMeteringException(code);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            await ledger.ReleaseAsync(reservation.ReservationId, AiOutcome.Cancelled, CancellationToken.None);
            AiMeteringTelemetry.Finish(activity, scope, key, "cancelled", null, null, null);
            cancellationToken.ThrowIfCancellationRequested();
        }
        TResult result;
        try
        {
            result = await generate(cancellationToken);
        }
        catch (Exception error)
        {
            var outcome = error is OperationCanceledException && cancellationToken.IsCancellationRequested
                ? AiOutcome.Cancelled : AiOutcome.Error;
            var record = new AiUsageRecord(reservation.ReservationId, requestKey, scope, key,
                null, null, null, maximum, maximum, price.Currency, price.PriceVersion,
                outcome, false, startedAt, clock.GetUtcNow());
            try { await ledger.CommitAsync(record, CancellationToken.None); }
            catch { /* Preserve the original provider exception; ledger recovery is host-owned. */ }
            AiMeteringTelemetry.Finish(activity, scope, key, outcome.ToString().ToLowerInvariant(), null, null, maximum);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        GenerationUsage? usage = usageOf(result);
        decimal? estimated = usage is null ? null : AiCostCalculator.Cost(price, usage);
        var completed = new AiUsageRecord(reservation.ReservationId, requestKey, scope, key,
            responseModelOf(result), usage?.InputTokens, usage?.OutputTokens,
            maximum, estimated ?? maximum, price.Currency, price.PriceVersion,
            outcomeOf(result), estimated is not null, startedAt, clock.GetUtcNow());
        await ledger.CommitAsync(completed, CancellationToken.None);
        AiMeteringTelemetry.Finish(activity, scope, key, completed.Outcome.ToString().ToLowerInvariant(),
            null, usage, completed.EstimatedCost);
        return result;
    }
}

public sealed class MeteredTextGeneration(ITextGenerationModel inner, IAiBudgetLedger ledger,
    AiPriceCatalog catalog, AiMeteringOptions options, TimeProvider clock)
{
    public Task<TextGenerationResult> GenerateTextAsync(AiUsageScope scope, string requestKey,
        TextGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return AiMeteringFlow.RunAsync(scope, requestKey, options.TextModel, request.MaxOutputTokens,
            [request.Instructions, request.Input], ct => inner.GenerateTextAsync(request, ct),
            result => result.Usage, result => result.Model,
            result => result.Complete ? AiOutcome.Completed : AiOutcome.Incomplete,
            ledger, catalog, clock, cancellationToken);
    }
}

public sealed class MeteredStructuredGeneration(IStructuredGenerationModel inner, IAiBudgetLedger ledger,
    AiPriceCatalog catalog, AiMeteringOptions options, TimeProvider clock)
{
    public Task<StructuredGenerationResult> GenerateStructuredAsync(AiUsageScope scope, string requestKey,
        StructuredGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Regex.IsMatch(request.SchemaName, "^[A-Za-z0-9_-]{1,64}$") ||
            string.IsNullOrWhiteSpace(request.Instructions) || request.Instructions.Length > 32_000 ||
            string.IsNullOrWhiteSpace(request.Input) || request.Input.Length > 32_000 ||
            request.MaxOutputTokens is < 1 or > 16_384 || request.Schema.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new ArgumentException("Invalid structured-generation request bounds.");
        string schema = request.Schema.GetRawText();
        if (schema.Length > 32_000) throw new ArgumentException("Invalid structured-generation schema bounds.");
        return AiMeteringFlow.RunAsync(scope, requestKey, options.StructuredModel, request.MaxOutputTokens,
            [request.Instructions, request.Input, schema], ct => inner.GenerateStructuredAsync(request, ct),
            result => result.Usage, result => result.Model,
            result => result.Status switch
            {
                StructuredGenerationStatus.Completed => AiOutcome.Completed,
                StructuredGenerationStatus.Incomplete => AiOutcome.Incomplete,
                _ => AiOutcome.Refused
            }, ledger, catalog, clock, cancellationToken);
    }
}

public static class AiMeteringRegistration
{
    public static IServiceCollection AddAiMetering(this IServiceCollection services,
        AiPriceCatalog catalog, Action<AiMeteringOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new AiMeteringOptions();
        configure(options);
        services.AddSingleton(catalog);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<MeteredTextGeneration>();
        services.AddTransient<MeteredStructuredGeneration>();
        return services;
    }
}
