using System.Text.Json;
using Datamatrix_Notepad.Models;
using Datamatrix_Notepad.Services.State;
using Xunit;

namespace Datamatrix_Notepad.Tests.State;

public sealed class AppStateTests
{
    [Fact]
    public async Task LoadAsync_UsesDefaultsWhenStorageIsEmpty()
    {
        var store = new FakeAppStateStore();
        var service = new AppStateService(store);

        var result = await service.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(AppState.CurrentSchemaVersion, service.State.SchemaVersion);
        Assert.Empty(service.State.Notes);
        Assert.Equal("\r\n", service.State.SerialSettings.Suffix);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task LoadAsync_RestoresValidVersionedState()
    {
        var noteId = Guid.NewGuid();
        var expected = AppState.CreateDefault() with
        {
            ActiveNoteId = noteId,
            SerialSettings = SerialSettings.Default with { Encoding = "utf-16be" },
            Notes =
            [
                new Note(
                    noteId,
                    "Тест",
                    "CODE",
                    DateTimeOffset.Parse("2026-07-25T10:00:00Z"),
                    DateTimeOffset.Parse("2026-07-25T10:01:00Z"))
            ]
        };
        var store = new FakeAppStateStore
        {
            StoredValue = JsonSerializer.Serialize(
                expected,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var service = new AppStateService(store);

        var result = await service.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(noteId, service.State.ActiveNoteId);
        Assert.Equal("Тест", Assert.Single(service.State.Notes).Title);
        Assert.Equal("utf-16be", service.State.SerialSettings.Encoding);
        Assert.Equal(0, store.WriteCount);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("""
                {
                  "schemaVersion": 99,
                  "activeNoteId": null,
                  "notes": [],
                  "serialSettings": {
                    "portAlias": "Сканер",
                    "usbVendorId": null,
                    "usbProductId": null,
                    "baudRate": 9600,
                    "dataBits": 8,
                    "parity": "none",
                    "stopBits": 1,
                    "flowControl": "none",
                    "encoding": "utf-8",
                    "prefix": "",
                    "suffix": "\r\n"
                  }
                }
                """)]
    public async Task LoadAsync_DoesNotOverwriteInvalidOrUnknownState(string storedValue)
    {
        var store = new FakeAppStateStore { StoredValue = storedValue };
        var service = new AppStateService(store);

        var result = await service.LoadAsync();

        Assert.False(result.IsSuccess);
        Assert.Empty(service.State.Notes);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task ScheduleSaveAsync_UsesApprovedDebounce()
    {
        var store = new FakeAppStateStore();
        var service = new AppStateService(store);
        await service.LoadAsync();

        await service.ScheduleSaveAsync();

        Assert.Equal(1, store.ScheduledWriteCount);
        Assert.Equal(TimeSpan.FromMilliseconds(300), store.LastDebounce);
        Assert.Contains("\"schemaVersion\":1", store.StoredValue);
    }

    [Fact]
    public async Task SaveNowAsync_BypassesDebounce()
    {
        var store = new FakeAppStateStore();
        var service = new AppStateService(store);
        await service.LoadAsync();

        await service.SaveNowAsync();

        Assert.Equal(1, store.ImmediateWriteCount);
        Assert.Equal(0, store.ScheduledWriteCount);
    }

    private sealed class FakeAppStateStore : IAppStateStore
    {
        public string? StoredValue { get; set; }

        public int ScheduledWriteCount { get; private set; }

        public int ImmediateWriteCount { get; private set; }

        public int WriteCount => ScheduledWriteCount + ImmediateWriteCount;

        public TimeSpan? LastDebounce { get; private set; }

        public ValueTask<string?> ReadAsync(string key) =>
            ValueTask.FromResult(StoredValue);

        public ValueTask ScheduleWriteAsync(
            string key,
            string value,
            TimeSpan debounce)
        {
            StoredValue = value;
            LastDebounce = debounce;
            ScheduledWriteCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteNowAsync(string key, string value)
        {
            StoredValue = value;
            ImmediateWriteCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
