using Datamatrix_Notepad.Services.Serial;
using Xunit;

namespace Datamatrix_Notepad.Tests.Serial;

public sealed class SerialPortServiceTests
{
    [Fact]
    public void Defaults_MatchApprovedScannerConfiguration()
    {
        var options = SerialConnectionOptions.Default;

        Assert.Equal(9600, options.BaudRate);
        Assert.Equal(8, options.DataBits);
        Assert.Equal("none", options.Parity);
        Assert.Equal(1, options.StopBits);
        Assert.Equal("none", options.FlowControl);
        Assert.Equal(string.Empty, options.Prefix);
        Assert.Equal("\r\n", options.Suffix);
    }

    [Theory]
    [InlineData(0, 8, "none", 1, "none", "\r\n")]
    [InlineData(9600, 6, "none", 1, "none", "\r\n")]
    [InlineData(9600, 8, "mark", 1, "none", "\r\n")]
    [InlineData(9600, 8, "none", 3, "none", "\r\n")]
    [InlineData(9600, 8, "none", 1, "software", "\r\n")]
    [InlineData(9600, 8, "none", 1, "none", "")]
    public void Validate_RejectsUnsupportedConnectionOptions(
        int baudRate,
        int dataBits,
        string parity,
        int stopBits,
        string flowControl,
        string suffix)
    {
        var options = new SerialConnectionOptions(
            baudRate,
            dataBits,
            parity,
            stopBits,
            flowControl,
            string.Empty,
            suffix);

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void MakeControlCharactersVisible_EscapesDiagnosticCharacters()
    {
        var result = SerialTextFormatter.MakeControlCharactersVisible("ABC\r\n\tDEF");

        Assert.Equal(@"ABC\r\n\tDEF", result);
    }
}
