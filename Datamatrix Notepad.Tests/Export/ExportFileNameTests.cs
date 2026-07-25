using Datamatrix_Notepad.Services.Export;
using Xunit;

namespace Datamatrix_Notepad.Tests.Export;

public sealed class ExportFileNameTests
{
    private readonly ExportFileNameService _service = new();

    [Theory]
    [InlineData(
        "Партия 00ЦБ-123456 от 25.07.2026",
        "123456;25.07.2026.txt")]
    [InlineData("Партия 00ЦБ-123456", "123456;.txt")]
    [InlineData("От 25.07.2026", ";25.07.2026.txt")]
    [InlineData("Произвольное название", ";.txt")]
    [InlineData("00ЦБ-12345 от 31.02.2026", ";.txt")]
    public void CreateFileName_MatchesApprovedExamples(
        string title,
        string expected)
    {
        var fileName = _service.CreateFileName(title);

        Assert.Equal(expected, fileName);
    }

    [Fact]
    public void CreateFileName_UsesFirstMatchingPartAndFirstValidDate()
    {
        var fileName = _service.CreateFileName(
            "00ЦБ-111111 31.02.2026 00ЦБ-222222 29.02.2024");

        Assert.Equal("111111;29.02.2024.txt", fileName);
    }

    [Theory]
    [InlineData("00ЦБ-1234567", ";.txt")]
    [InlineData("00ЦБ-12345", ";.txt")]
    [InlineData("00цб-123456", ";.txt")]
    public void CreateFileName_RequiresExactPartPattern(
        string title,
        string expected)
    {
        Assert.Equal(expected, _service.CreateFileName(title));
    }

    [Theory]
    [InlineData("1.01.2026")]
    [InlineData("01.1.2026")]
    [InlineData("01.01.26")]
    [InlineData("101.01.2026")]
    [InlineData("01.01.20260")]
    public void CreateFileName_RequiresExactDateFormat(string title)
    {
        Assert.Equal(";.txt", _service.CreateFileName(title));
    }

    [Theory]
    [InlineData("29.02.2024", ";29.02.2024.txt")]
    [InlineData("29.02.2025", ";.txt")]
    [InlineData("00.01.2026", ";.txt")]
    [InlineData("31.12.2026", ";31.12.2026.txt")]
    public void CreateFileName_ValidatesCalendarDate(
        string title,
        string expected)
    {
        Assert.Equal(expected, _service.CreateFileName(title));
    }

    [Fact]
    public void CreateFileName_RejectsNullTitle()
    {
        Assert.Throws<ArgumentNullException>(
            () => _service.CreateFileName(null!));
    }
}
