namespace AIFramework.Core.Models;

public sealed record TextGenerationRequest(string Instructions, string Input, int MaxOutputTokens = 1024)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Instructions) || Instructions.Length > 32_000 ||
            string.IsNullOrWhiteSpace(Input) || Input.Length > 200_000 ||
            MaxOutputTokens is < 1 or > 16_384)
            throw new ArgumentException("Invalid text-generation request bounds.");
    }
}

public sealed record TextGenerationResult(string Text, string Provider)
{
    public string? Model { get; init; }
    public GenerationUsage? Usage { get; init; }
    public bool Complete { get; init; } = true;
}
