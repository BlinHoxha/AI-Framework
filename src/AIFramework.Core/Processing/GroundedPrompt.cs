using AIFramework.Core.Models;

namespace AIFramework.Core.Processing;

public static class GroundedPrompt
{
    public static string FormatEvidence(IReadOnlyCollection<KnowledgeChunk> context)
        => string.Join("\n\n", context.Select((chunk, index) =>
            $"[{index + 1}] Document {chunk.DocumentId}, chunk {chunk.ChunkNumber}: {chunk.Content}"));

    public static string AddReviewNotice(string answer, bool requiresHumanReview)
        => requiresHumanReview ? answer + "\nHuman verification is required for this document profile." : answer;
}
