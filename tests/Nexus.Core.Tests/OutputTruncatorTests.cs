using System.Text;
using Nexus.Core.Services;

namespace Nexus.Core.Tests;

public class OutputTruncatorTests
{
    [Fact]
    public void Truncate_UnderLimit_ReturnsUnchanged()
    {
        // Arrange
        var lines = Enumerable.Range(1, 10).Select(i => $"line {i}");
        var input = string.Join('\n', lines);

        // Act
        var result = OutputTruncator.Truncate(input, maxLines: 200, maxBytes: 32000);

        // Assert
        Assert.False(result.WasTruncated);
        Assert.Equal(input, result.Content);
        Assert.Equal(10, result.OriginalLines);
        Assert.True(result.OriginalBytes < 32000);
    }

    [Fact]
    public void Truncate_ExceedsLines_HeadTailSplit()
    {
        // Arrange: 300 lines, each "line NNN\n"
        var lines = Enumerable.Range(1, 300).Select(i => $"line {i:D3}").ToArray();
        var input = string.Join('\n', lines);

        // Act
        var result = OutputTruncator.Truncate(input, maxLines: 200, maxBytes: 32000);

        // Assert
        Assert.True(result.WasTruncated);
        Assert.Equal(300, result.OriginalLines);

        // Head: lines 1..100, Tail: lines 201..300
        var content = result.Content;
        Assert.Contains("line 001", content);  // first line of head
        Assert.Contains("line 100", content);  // last line of head
        Assert.Contains("line 201", content);  // first line of tail
        Assert.Contains("line 300", content);  // last line of tail
        Assert.Contains("[...truncated 100 lines", content);

        // Lines 101..200 (omitted middle) must NOT appear
        Assert.DoesNotContain("line 101", content);
        Assert.DoesNotContain("line 200", content);
    }

    [Fact]
    public void Truncate_ExceedsBytes_ByteTruncation()
    {
        // Arrange: 5 lines that exceed byte limit but stay under line limit
        // Each line is ~100 ASCII bytes; 5 lines ≈ 500 bytes, well under 200 lines
        var lines = Enumerable.Range(1, 5).Select(i => new string('A', 99));
        var input = string.Join('\n', lines);
        var originalBytes = Encoding.UTF8.GetByteCount(input);

        // sanity: under line limit, over byte limit
        Assert.True(originalBytes > 100);

        // Act
        var result = OutputTruncator.Truncate(input, maxLines: 200, maxBytes: 100);

        // Assert
        Assert.True(result.WasTruncated);
        Assert.Equal(5, result.OriginalLines);
        Assert.Equal(originalBytes, result.OriginalBytes);
        Assert.Contains("[...truncated", result.Content);
        // Verify the text portion before the notice fits within the byte limit
        var noticeIndex = result.Content.IndexOf("\n[...truncated", StringComparison.Ordinal);
        Assert.True(noticeIndex >= 0);
        var textPart = result.Content[..noticeIndex];
        Assert.True(Encoding.UTF8.GetByteCount(textPart) <= 100);
    }

    [Fact]
    public void Truncate_Empty_ReturnsUnchanged()
    {
        // Act
        var result = OutputTruncator.Truncate(string.Empty, maxLines: 200, maxBytes: 32000);

        // Assert
        Assert.False(result.WasTruncated);
        Assert.Equal(string.Empty, result.Content);
        Assert.Equal(0, result.OriginalLines);
        Assert.Equal(0, result.OriginalBytes);
    }

    [Fact]
    public void Truncate_ExactlyAtLimit_ReturnsUnchanged()
    {
        // Arrange: exactly 200 lines
        var lines = Enumerable.Range(1, 200).Select(i => $"line {i}");
        var input = string.Join('\n', lines);

        // Act
        var result = OutputTruncator.Truncate(input, maxLines: 200, maxBytes: 32000);

        // Assert
        Assert.False(result.WasTruncated);
        Assert.Equal(input, result.Content);
        Assert.Equal(200, result.OriginalLines);
    }
}
