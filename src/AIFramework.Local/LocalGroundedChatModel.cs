using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;

namespace AIFramework.Local;

public sealed class LocalGroundedChatModel : IChatModel
{
    public Task<GroundedGenerationResult> GenerateAsync(GroundedGenerationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string evidence = string.Join(
            Environment.NewLine,
            request.Context.Take(3).Select((chunk, index) => $"[{index + 1}] {chunk.Content}"));
        string reviewNotice = request.Profile.RequiresHumanReview
            ? $"{Environment.NewLine}Human verification is required for this document profile."
            : string.Empty;

        return Task.FromResult(new GroundedGenerationResult(
            $"Relevant evidence from the authorized documents:{Environment.NewLine}{evidence}{reviewNotice}",
            "local-extractive"));
    }
}
