using System.Text.RegularExpressions;

namespace AIFramework.Ollama;

public sealed record OllamaOptions(Uri Endpoint, string Model, int ContextTokens = 8192,
    int TimeoutSeconds = 120, bool? Think = false)
{
    public void Validate()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri ||
            Endpoint.Scheme is not ("http" or "https") || !Endpoint.IsLoopback ||
            Endpoint.UserInfo.Length != 0 || Endpoint.Query.Length != 0 ||
            Endpoint.Fragment.Length != 0 || Endpoint.AbsolutePath != "/")
            throw new ArgumentException("Ollama requires a loopback endpoint root.", nameof(Endpoint));
        if (Model is null || !Regex.IsMatch(Model, "^[A-Za-z0-9._:/-]{1,200}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid Ollama model name.", nameof(Model));
        if (ContextTokens is < 512 or > 131_072)
            throw new ArgumentOutOfRangeException(nameof(ContextTokens));
        if (TimeoutSeconds is < 1 or > 600)
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
    }
}
