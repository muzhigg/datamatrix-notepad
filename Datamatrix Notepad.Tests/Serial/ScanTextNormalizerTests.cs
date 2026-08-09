using Datamatrix_Notepad.Services.Serial;
using Xunit;

namespace Datamatrix_Notepad.Tests.Serial;

public sealed class ScanTextNormalizerTests
{
    [Theory]
    [InlineData("ABC", "ABC")]
    [InlineData("A\u001DB\u001DC", "ABC")]
    [InlineData("\u001D", "")]
    [InlineData("A\r\n\tB", "A\r\n\tB")]
    public void NormalizeCompletedCode_RemovesOnlyGroupSeparators(
        string value,
        string expected)
    {
        var result = ScanTextNormalizer.NormalizeCompletedCode(value);

        Assert.Equal(expected, result);
    }
}
