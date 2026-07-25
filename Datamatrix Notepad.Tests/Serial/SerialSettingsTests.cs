using Datamatrix_Notepad.Models;
using Datamatrix_Notepad.Services.Serial;
using Datamatrix_Notepad.Services.State;
using Xunit;

namespace Datamatrix_Notepad.Tests.Serial;

public sealed class SerialSettingsTests
{
    [Fact]
    public void DefaultSettings_MatchApprovedScannerConfiguration()
    {
        var settings = SerialSettings.Default;

        Assert.Equal("Сканер", settings.PortAlias);
        Assert.Null(settings.UsbVendorId);
        Assert.Null(settings.UsbProductId);
        Assert.Equal(9600, settings.BaudRate);
        Assert.Equal(8, settings.DataBits);
        Assert.Equal("none", settings.Parity);
        Assert.Equal(1, settings.StopBits);
        Assert.Equal("none", settings.FlowControl);
        Assert.Equal("utf-8", settings.Encoding);
        Assert.Equal(string.Empty, settings.Prefix);
        Assert.Equal("\r\n", settings.Suffix);
    }

    [Theory]
    [InlineData(@"", "")]
    [InlineData(@"\r", "\r")]
    [InlineData(@"\n", "\n")]
    [InlineData(@"\r\n", "\r\n")]
    [InlineData(@"START\t", "START\t")]
    [InlineData(@"literal\\value", "literal\\value")]
    public void TryParseControlCharacters_ParsesSupportedEscapes(
        string displayValue,
        string expected)
    {
        var isValid = SerialTextFormatter.TryParseControlCharacters(
            displayValue,
            out var value);

        Assert.True(isValid);
        Assert.Equal(expected, value);
        Assert.Equal(
            displayValue,
            SerialTextFormatter.MakeControlCharactersVisible(value));
    }

    [Theory]
    [InlineData(@"\")]
    [InlineData(@"\x")]
    [InlineData(@"CODE\u1234")]
    public void TryParseControlCharacters_RejectsUnknownEscapes(string displayValue)
    {
        var isValid = SerialTextFormatter.TryParseControlCharacters(
            displayValue,
            out var value);

        Assert.False(isValid);
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public async Task UpdateSerialSettingsAsync_UpdatesStateAndSchedulesSave()
    {
        var store = new FakeAppStateStore();
        var service = new AppStateService(store);
        await service.LoadAsync();
        var settings = SerialSettings.Default with
        {
            PortAlias = "Складской сканер",
            UsbVendorId = 0x1234,
            UsbProductId = 0x5678,
            BaudRate = 115200,
            Prefix = "DM:",
            Suffix = "\r"
        };

        await service.UpdateSerialSettingsAsync(settings);

        Assert.Equal(settings, service.State.SerialSettings);
        Assert.Equal(1, store.ScheduledWriteCount);
        Assert.Equal(0, store.ImmediateWriteCount);
    }

    [Fact]
    public async Task UpdateSerialSettingsAsync_CanPersistSelectedPortImmediately()
    {
        var store = new FakeAppStateStore();
        var service = new AppStateService(store);
        await service.LoadAsync();
        var settings = SerialSettings.Default with
        {
            UsbVendorId = 0x1234,
            UsbProductId = 0x5678
        };

        await service.UpdateSerialSettingsAsync(settings, saveImmediately: true);

        Assert.Equal(0, store.ScheduledWriteCount);
        Assert.Equal(1, store.ImmediateWriteCount);
    }

    [Fact]
    public void Validate_RejectsEmptySuffix()
    {
        var settings = SerialSettings.Default with { Suffix = string.Empty };

        Assert.Throws<ArgumentException>(settings.Validate);
    }

    private sealed class FakeAppStateStore : IAppStateStore
    {
        public int ScheduledWriteCount { get; private set; }

        public int ImmediateWriteCount { get; private set; }

        public ValueTask<string?> ReadAsync(string key) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask ScheduleWriteAsync(
            string key,
            string value,
            TimeSpan debounce)
        {
            ScheduledWriteCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteNowAsync(string key, string value)
        {
            ImmediateWriteCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
