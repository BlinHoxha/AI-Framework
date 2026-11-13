using System.Net;
using System.Text.Json;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using AIFramework.Ollama;
using Microsoft.Extensions.DependencyInjection;

namespace AIFramework.UnitTests;

public sealed class OllamaGenerationModelTests
{
    private static readonly OllamaOptions Options = new(new Uri("http://127.0.0.1:11434/"), "qwen3:1.7b");
    private static readonly TextGenerationRequest TextRequest = new("system", "input", 100);
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}
        """).RootElement.Clone();
    private static readonly StructuredGenerationRequest StructuredRequest = new("result", "system", "input", Schema, 100);

    [Fact]
    public async Task Text_request_and_metadata_match_native_api()
    {
        using var handler = new Handler(Response("stop", "hello", "qwen3:1.7b", 11, 4));
        var result = await Model(handler).GenerateTextAsync(TextRequest, default);
        Assert.Equal("hello", result.Text);
        Assert.Equal("ollama", result.Provider);
        Assert.Equal("qwen3:1.7b", result.Model);
        Assert.Equal(new GenerationUsage(11, 4), result.Usage);
        Assert.True(result.Complete);
        Assert.Equal("/api/chat", Assert.Single(handler.Paths));
        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal("qwen3:1.7b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("user", root.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.Equal("input", root.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(100, root.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.False(root.GetProperty("options").TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("format", out _));
    }

    [Fact]
    public async Task Null_think_is_omitted()
    {
        using var handler = new Handler(Response("stop", "ok"));
        await Model(handler, Options with { Think = null }).GenerateTextAsync(TextRequest, default);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.False(body.RootElement.TryGetProperty("think", out _));
    }

    [Theory]
    [InlineData("stop", true)]
    [InlineData("length", false)]
    public async Task Text_completion_maps_reason(string reason, bool complete)
    {
        using var handler = new Handler(Response(reason, "answer"));
        Assert.Equal(complete, (await Model(handler).GenerateTextAsync(TextRequest, default)).Complete);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    public async Task Text_rejects_other_completion_reasons(string reason)
    {
        using var handler = new Handler(Response(reason, "answer"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Model(handler).GenerateTextAsync(TextRequest, default));
    }

    [Theory]
    [InlineData("{\"done\":false,\"done_reason\":\"stop\",\"message\":{\"content\":\"x\"}}")]
    [InlineData("{\"done\":true,\"done_reason\":\"stop\",\"message\":{}}")]
    [InlineData("{\"done\":true,\"done_reason\":\"stop\"}")]
    public async Task Text_rejects_missing_completion_or_content(string response)
    {
        using var handler = new Handler(response);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Model(handler).GenerateTextAsync(TextRequest, default));
    }

    [Fact]
    public async Task Usage_ignores_missing_negative_and_non_integer_counts()
    {
        using var handler = new Handler("""{"done":true,"done_reason":"stop","message":{"content":"ok"},"prompt_eval_count":-1,"eval_count":"four"}""");
        var result = await Model(handler).GenerateTextAsync(TextRequest, default);
        Assert.Equal(new GenerationUsage(null, null), result.Usage);
        Assert.Null(result.Model);
    }

    [Fact]
    public async Task Structured_request_sends_schema_and_zero_temperature()
    {
        using var handler = new Handler(Response("stop", "{\"name\":\"Ada\"}", "qwen3:1.7b", 8, 3));
        var result = await Model(handler).GenerateStructuredAsync(StructuredRequest, default);
        Assert.Equal(StructuredGenerationStatus.Completed, result.Status);
        Assert.Equal("{\"name\":\"Ada\"}", result.OutputJson);
        Assert.Equal("ollama", result.Provider);
        Assert.Equal("qwen3:1.7b", result.Model);
        Assert.Equal(new GenerationUsage(8, 3), result.Usage);
        Assert.Equal("/api/chat", Assert.Single(handler.Paths));
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(Schema.GetRawText(), body.RootElement.GetProperty("format").GetRawText());
        Assert.Equal(0, body.RootElement.GetProperty("options").GetProperty("temperature").GetInt32());
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Structured_length_returns_no_partial_json()
    {
        using var handler = new Handler(Response("length", "{\"name\":"));
        var result = await Model(handler).GenerateStructuredAsync(StructuredRequest, default);
        Assert.Equal(StructuredGenerationStatus.Incomplete, result.Status);
        Assert.Null(result.OutputJson);
    }

    [Theory]
    [InlineData("{\"name\":3}")]
    [InlineData("{\"name\":\"Ada\",\"extra\":1}")]
    [InlineData("{\"name\":")]
    public async Task Structured_revalidates_output(string content)
    {
        using var handler = new Handler(Response("stop", content));
        var error = await Assert.ThrowsAsync<StructuredGenerationException>(
            () => Model(handler).GenerateStructuredAsync(StructuredRequest, default));
        Assert.Equal("invalid_response", error.Code);
    }

    [Fact]
    public async Task Structured_rejects_unknown_reason_and_missing_content()
    {
        using var wrong = new Handler(Response("error", "{}"));
        Assert.Equal("invalid_response", (await Assert.ThrowsAsync<StructuredGenerationException>(
            () => Model(wrong).GenerateStructuredAsync(StructuredRequest, default))).Code);
        using var missing = new Handler("""{"done":true,"done_reason":"stop","message":{}}""");
        Assert.Equal("invalid_response", (await Assert.ThrowsAsync<StructuredGenerationException>(
            () => Model(missing).GenerateStructuredAsync(StructuredRequest, default))).Code);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Non_success_status_throws_without_body(int status)
    {
        using var handler = new Handler("secret response body", (HttpStatusCode)status);
        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Model(handler).GenerateTextAsync(TextRequest, default));
        Assert.DoesNotContain("secret response body", error.Message);
    }

    [Fact]
    public async Task Response_size_limits_are_enforced()
    {
        using var text = new Handler(new string('x', 1_048_577));
        Assert.Equal("response_too_large", (await Assert.ThrowsAsync<StructuredGenerationException>(
            () => Model(text).GenerateTextAsync(TextRequest, default))).Code);
        using var structured = new Handler(new string('x', 131_073));
        Assert.Equal("response_too_large", (await Assert.ThrowsAsync<StructuredGenerationException>(
            () => Model(structured).GenerateStructuredAsync(StructuredRequest, default))).Code);
    }

    [Fact]
    public async Task Invalid_json_is_sanitized()
    {
        using var handler = new Handler("secret invalid response {");
        var error = await Assert.ThrowsAsync<StructuredGenerationException>(
            () => Model(handler).GenerateStructuredAsync(StructuredRequest, default));
        Assert.Equal("invalid_response", error.Code);
        Assert.DoesNotContain("secret invalid response", error.Message);
    }

    [Fact]
    public async Task Context_bound_rejects_without_network()
    {
        using var handler = new Handler(Response("stop", "ok"));
        var request = TextRequest with { Input = new string('a', 500), MaxOutputTokens = 100 };
        await Assert.ThrowsAsync<ArgumentException>(() => Model(handler, Options with { ContextTokens = 512 })
            .GenerateTextAsync(request, default));
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Structured_context_bound_rejects_without_network()
    {
        using var handler = new Handler(Response("stop", "{}"));
        var request = StructuredRequest with { Input = new string('a', 500) };
        await Assert.ThrowsAsync<ArgumentException>(() => Model(handler, Options with { ContextTokens = 512 })
            .GenerateStructuredAsync(request, default));
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Invalid_structured_request_rejects_without_network()
    {
        using var handler = new Handler(Response("stop", "{}"));
        var model = Model(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => model.GenerateStructuredAsync(
            StructuredRequest with { SchemaName = "bad name" }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => model.GenerateStructuredAsync(
            StructuredRequest with { MaxOutputTokens = 4097 }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => model.GenerateStructuredAsync(
            StructuredRequest with { Instructions = " " }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => model.GenerateStructuredAsync(
            StructuredRequest with { Input = " " }, default));
        using var invalid = JsonDocument.Parse("""{"type":"string"}""");
        await Assert.ThrowsAsync<ArgumentException>(() => model.GenerateStructuredAsync(
            StructuredRequest with { Schema = invalid.RootElement }, default));
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Text_invalid_json_is_sanitized()
    {
        using var handler = new Handler("secret invalid response {");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Model(handler).GenerateTextAsync(TextRequest, default));
        Assert.DoesNotContain("secret invalid response", error.Message);
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("http://user:pass@127.0.0.1:11434/")]
    [InlineData("http://127.0.0.1:11434/?x=1")]
    [InlineData("http://127.0.0.1:11434/#x")]
    [InlineData("http://127.0.0.1:11434/extra")]
    public void Invalid_endpoints_are_rejected(string endpoint)
        => Assert.Throws<ArgumentException>(() => (Options with { Endpoint = new Uri(endpoint) }).Validate());

    [Theory]
    [InlineData("")]
    [InlineData("model name")]
    [InlineData("model?bad")]
    public void Invalid_models_are_rejected(string model)
        => Assert.Throws<ArgumentException>(() => (Options with { Model = model }).Validate());

    [Fact]
    public void Option_bounds_are_enforced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Options with { ContextTokens = 511 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Options with { ContextTokens = 131_073 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Options with { TimeoutSeconds = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Options with { TimeoutSeconds = 601 }).Validate());
        (Options with { Endpoint = new Uri("http://localhost:11434/") }).Validate();
        (Options with { Endpoint = new Uri("http://[::1]:11434/") }).Validate();
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(404, false)]
    public async Task Model_availability_uses_show_endpoint(int status, bool expected)
    {
        using var handler = new Handler("{}", (HttpStatusCode)status);
        Assert.Equal(expected, await Model(handler).IsModelAvailableAsync(default));
        Assert.Equal("/api/show", Assert.Single(handler.Paths));
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("qwen3:1.7b", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Model_availability_other_failure_throws()
    {
        using var handler = new Handler("error", HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => Model(handler).IsModelAvailableAsync(default));
    }

    [Fact]
    public void Registration_exposes_one_model_through_exactly_two_generation_interfaces()
    {
        using var provider = new ServiceCollection().AddOllamaModels(Options).BuildServiceProvider();
        var text = provider.GetRequiredService<ITextGenerationModel>();
        var structured = provider.GetRequiredService<IStructuredGenerationModel>();
        Assert.Same(text, structured);
        Assert.Null(provider.GetService<IChatModel>());
        Assert.Null(provider.GetService<ITextEmbeddingModel>());
    }

    private static OllamaGenerationModel Model(Handler handler, OllamaOptions? options = null)
        => new(options ?? Options, new HttpClient(handler));

    private static string Response(string reason, string content, string? model = null, int? input = null, int? output = null)
        => JsonSerializer.Serialize(new { done = true, done_reason = reason, message = new { content }, model,
            prompt_eval_count = input, eval_count = output });

    private sealed class Handler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(response) };
        }
    }
}
