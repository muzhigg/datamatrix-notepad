using Datamatrix_Notepad.Services.Serial;
using Xunit;

namespace Datamatrix_Notepad.Tests.Serial;

public sealed class ScanFrameParserTests
{
    [Fact]
    public void Append_CompletesCodeWhenSuffixIsSplitAcrossChunks()
    {
        var parser = new ScanFrameParser(prefix: string.Empty, suffix: "\r\n");

        var firstResult = parser.Append("ABC123\r");
        var secondResult = parser.Append("\n");

        Assert.Empty(firstResult);
        Assert.Equal(["ABC123"], secondResult);
    }

    [Fact]
    public void Append_ReturnsMultipleCodesFromOneChunk()
    {
        var parser = new ScanFrameParser(prefix: string.Empty, suffix: "\r\n");

        var result = parser.Append("FIRST\r\nSECOND\r\n");

        Assert.Equal(["FIRST", "SECOND"], result);
    }

    [Fact]
    public void Append_PreservesDuplicateCodes()
    {
        var parser = new ScanFrameParser(prefix: string.Empty, suffix: "\n");

        var result = parser.Append("SAME\nSAME\n");

        Assert.Equal(["SAME", "SAME"], result);
    }

    [Fact]
    public void Append_DiscardsNoiseAndRemovesConfiguredPrefix()
    {
        var parser = new ScanFrameParser(prefix: "[)", suffix: "\r");

        var firstResult = parser.Append("noise[");
        var secondResult = parser.Append(")ABC\rignored[)DEF\r");

        Assert.Empty(firstResult);
        Assert.Equal(["ABC", "DEF"], secondResult);
    }

    [Fact]
    public void Append_IgnoresEmptyCompletedCodes()
    {
        var parser = new ScanFrameParser(prefix: string.Empty, suffix: "\n");

        var result = parser.Append("\nVALUE\n\n");

        Assert.Equal(["VALUE"], result);
    }

    [Fact]
    public void Reset_DiscardsAnIncompleteCode()
    {
        var parser = new ScanFrameParser(prefix: string.Empty, suffix: "\r\n");
        parser.Append("INCOMPLETE");

        parser.Reset();
        var result = parser.Append("COMPLETE\r\n");

        Assert.Equal(["COMPLETE"], result);
    }

    [Fact]
    public void Constructor_RejectsEmptySuffix()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ScanFrameParser(prefix: string.Empty, suffix: string.Empty));

        Assert.Equal("suffix", exception.ParamName);
    }

    [Fact]
    public void Append_RejectsAndResetsOversizedIncompleteFrame()
    {
        var parser = new ScanFrameParser(prefix: string.Empty, suffix: "\r\n");
        var oversizedChunk = new string(
            'X',
            ScanFrameParser.MaximumBufferedLength + 1);

        Assert.Throws<InvalidDataException>(
            () => parser.Append(oversizedChunk));

        var result = parser.Append("RECOVERED\r\n");
        Assert.Equal(["RECOVERED"], result);
    }
}
