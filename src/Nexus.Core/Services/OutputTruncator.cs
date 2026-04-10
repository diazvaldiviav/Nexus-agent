using System.Text;

namespace Nexus.Core.Services;

/// <summary>
/// Represents the result of a truncation operation, preserving metadata about the original output.
/// </summary>
public record TruncatedOutput(
    string Content,
    bool WasTruncated,
    int OriginalLines,
    int OriginalBytes);

/// <summary>
/// Truncates large tool output using a head/tail strategy for line overflow,
/// and byte-safe truncation for byte overflow. Stateless and pure.
/// </summary>
public static class OutputTruncator
{
    /// <summary>
    /// Truncates the given output if it exceeds the configured line or byte limits.
    /// Uses head/tail splitting for line overflow and UTF-8 safe byte truncation for byte overflow.
    /// </summary>
    /// <param name="output">The raw tool output string. Null is treated as empty.</param>
    /// <param name="maxLines">Maximum number of lines to allow. 0 or negative disables line limit.</param>
    /// <param name="maxBytes">Maximum number of UTF-8 bytes to allow. 0 or negative disables byte limit.</param>
    public static TruncatedOutput Truncate(string? output, int maxLines, int maxBytes)
    {
        if (string.IsNullOrEmpty(output))
            return new TruncatedOutput(string.Empty, false, 0, 0);

        if (maxLines <= 0 && maxBytes <= 0)
            return new TruncatedOutput(output, false, CountLines(output), Encoding.UTF8.GetByteCount(output));

        var originalBytes = Encoding.UTF8.GetByteCount(output);
        var lines = output.Split('\n');
        var originalLines = lines.Length;

        if (maxLines > 0 && originalLines > maxLines)
        {
            var headCount = maxLines / 2;
            var tailCount = maxLines - headCount;
            var omitted = originalLines - maxLines;

            var separator = $"\n[...truncated {omitted} lines ({originalBytes} bytes total)...]\n";
            var content = string.Join('\n', lines[..headCount])
                          + separator
                          + string.Join('\n', lines[^tailCount..]);

            return new TruncatedOutput(content, true, originalLines, originalBytes);
        }

        if (maxBytes > 0 && originalBytes > maxBytes)
        {
            var content = TruncateToBytes(output, maxBytes, originalBytes);
            return new TruncatedOutput(content, true, originalLines, originalBytes);
        }

        return new TruncatedOutput(output, false, originalLines, originalBytes);
    }

    /// <summary>
    /// Counts the number of newline-delimited lines in a string.
    /// </summary>
    private static int CountLines(string text) => text.AsSpan().Count('\n') + 1;

    /// <summary>
    /// Truncates a string to at most maxBytes UTF-8 bytes without splitting multi-byte characters.
    /// Appends a truncation notice after the cut point.
    /// </summary>
    private static string TruncateToBytes(string text, int maxBytes, int originalBytes)
    {
        var byteCount = 0;
        var cutIndex = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var charBytes = Encoding.UTF8.GetByteCount(text, i, 1);
            if (byteCount + charBytes > maxBytes)
                break;

            byteCount += charBytes;
            cutIndex = i + 1;
        }

        var truncated = text[..cutIndex];
        var notice = $"\n[...truncated ({originalBytes} bytes total, showing first {maxBytes})...]";
        return truncated + notice;
    }
}
