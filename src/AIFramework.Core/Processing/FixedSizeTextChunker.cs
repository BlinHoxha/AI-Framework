using System.Text.RegularExpressions;
using AIFramework.Core.Abstractions;

namespace AIFramework.Core.Processing;

public sealed partial class FixedSizeTextChunker : ITextChunker
{
    private const int MaximumChunkLength = 1_200;

    public IReadOnlyCollection<string> Split(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        string normalized = WhitespaceRegex().Replace(content.Trim(), " ");
        List<string> chunks = [];

        for (int offset = 0; offset < normalized.Length;)
        {
            int length = Math.Min(MaximumChunkLength, normalized.Length - offset);
            if (offset + length < normalized.Length)
            {
                int sentenceEnd = normalized.LastIndexOfAny(['.', '!', '?', ';'], offset + length - 1, length);
                if (sentenceEnd > offset + MaximumChunkLength / 2)
                {
                    length = sentenceEnd - offset + 1;
                }
            }

            chunks.Add(normalized.Substring(offset, length).Trim());
            offset += length;
            while (offset < normalized.Length && char.IsWhiteSpace(normalized[offset]))
            {
                offset++;
            }
        }

        return chunks;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
