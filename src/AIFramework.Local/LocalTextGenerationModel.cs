using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;

namespace AIFramework.Local;

public sealed class LocalTextGenerationModel : ITextGenerationModel
{
    public Task<TextGenerationResult> GenerateTextAsync(TextGenerationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();
        string text = "Local text generation (no model): " + request.Input[..Math.Min(200, request.Input.Length)];
        return Task.FromResult(new TextGenerationResult(text, "local-echo"));
    }
}
