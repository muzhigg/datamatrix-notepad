using System.Text.Json;
using Datamatrix_Notepad.Services.Serial;
using Microsoft.JSInterop;
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
        Assert.Equal("utf-8", options.Encoding);
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
            "utf-8",
            string.Empty,
            suffix);

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void MakeControlCharactersVisible_EscapesDiagnosticCharacters()
    {
        var result = SerialTextFormatter.MakeControlCharactersVisible("ABC\u001D\r\n\tDEF");

        Assert.Equal(@"ABC\u001D\r\n\tDEF", result);
    }

    [Fact]
    public void Validate_UnsupportedEncoding_ThrowsArgumentException()
    {
        var options = SerialConnectionOptions.Default with { Encoding = "utf-16le" };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectAsync_Utf16BigEndian_PassesEncodingToBrowserModule(
        bool reconnect)
    {
        var module = new RecordingSerialModule();
        await using var service = new SerialPortService(new ModuleJsRuntime(module));
        var options = SerialConnectionOptions.Default with { Encoding = "utf-16be" };

        if (reconnect)
        {
            var result = await service.TryReconnectAsync(options, expectedPort: null);
            Assert.True(result.IsConnected);
        }
        else
        {
            var result = await service.SelectAndConnectAsync(options);
            Assert.True(result.IsConnected);
        }

        Assert.Equal("utf-16be", module.LastOpenOptions?.Encoding);
    }

    [Fact]
    public async Task ReceiveChunkAsync_GroupSeparator_PreservesCharacterForNote()
    {
        var module = new RecordingSerialModule();
        await using var service = new SerialPortService(new ModuleJsRuntime(module));
        SerialChunkEventArgs? received = null;
        service.ChunkReceived += (_, args) => received = args;

        var connection = await service.SelectAndConnectAsync(SerialConnectionOptions.Default);
        Assert.True(connection.IsConnected);

        const string raw = "0108\u001D91EE11\u001D92\r\n";
        await service.ReceiveChunkAsync(raw);

        var actual = Assert.IsType<SerialChunkEventArgs>(received);
        Assert.Equal(raw, actual.RawText);
        Assert.Equal(["0108\u001D91EE11\u001D92"], actual.CompletedCodes);
    }

    [Fact]
    public async Task ReceiveChunkAsync_GroupSeparatorOnly_ProducesCompletedCode()
    {
        var module = new RecordingSerialModule();
        await using var service = new SerialPortService(new ModuleJsRuntime(module));
        SerialChunkEventArgs? received = null;
        service.ChunkReceived += (_, args) => received = args;

        var connection = await service.SelectAndConnectAsync(SerialConnectionOptions.Default);
        Assert.True(connection.IsConnected);

        await service.ReceiveChunkAsync("\u001D\r\n");

        var actual = Assert.IsType<SerialChunkEventArgs>(received);
        Assert.Equal("\u001D\r\n", actual.RawText);
        Assert.Equal(["\u001D"], actual.CompletedCodes);
    }

    private sealed class ModuleJsRuntime(RecordingSerialModule module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args) =>
            identifier == "import"
                ? ValueTask.FromResult((TValue)(object)module)
                : throw new InvalidOperationException($"Unexpected JS call: {identifier}");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class RecordingSerialModule : IJSObjectReference
    {
        public SerialConnectionOptions? LastOpenOptions { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args)
        {
            if (identifier is "requestAndOpen" or "openPreviouslyGranted")
            {
                LastOpenOptions = Assert.IsType<SerialConnectionOptions>(args?[0]);
                var json = identifier == "requestAndOpen"
                    ? """{"cancelled":false,"usbVendorId":11334,"usbProductId":10826}"""
                    : """{"status":"connected","usbVendorId":11334,"usbProductId":10826}""";
                return ValueTask.FromResult(
                    JsonSerializer.Deserialize<TValue>(
                        json,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
