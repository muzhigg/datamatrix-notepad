using Datamatrix_Notepad.Services.State;
using Xunit;

namespace Datamatrix_Notepad.Tests.Integration;

public sealed class ScanToNoteTests
{
    [Fact]
    public async Task AppendScannedCodesAsync_AppendsEachCodeOnItsOwnLine()
    {
        var store = new FakeAppStateStore();
        var timeProvider = new TestTimeProvider(
            DateTimeOffset.Parse("2026-07-25T10:00:00Z"));
        var service = new AppStateService(store, timeProvider);
        await service.LoadAsync();
        var note = await service.CreateNoteAsync();
        await service.UpdateNoteAsync(note.Id, "Партия", "Вручную");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var writesBeforeScan = store.ScheduledWriteCount;

        var result = await service.AppendScannedCodesAsync(
            ["0104601234567890", "0104601234567891"]);

        Assert.Equal(ScanAppendStatus.Appended, result.Status);
        Assert.Equal(2, result.AppendedCount);
        Assert.Equal(
            "Вручную\n0104601234567890\n0104601234567891",
            service.ActiveNote?.Content);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-25T10:01:00Z"),
            service.ActiveNote?.UpdatedAtUtc);
        Assert.Equal(writesBeforeScan + 1, store.ScheduledWriteCount);
    }

    [Fact]
    public async Task AppendScannedCodesAsync_DoesNotCreateNoteWithoutActiveNote()
    {
        var store = new FakeAppStateStore();
        var service = new AppStateService(store);
        await service.LoadAsync();

        var result = await service.AppendScannedCodesAsync(["CODE"]);

        Assert.Equal(ScanAppendStatus.NoActiveNote, result.Status);
        Assert.Empty(service.State.Notes);
        Assert.Equal(0, store.ScheduledWriteCount);
        Assert.Equal(0, store.ImmediateWriteCount);
    }

    [Fact]
    public async Task AppendScannedCodesAsync_PreservesDuplicates()
    {
        var service = new AppStateService(new FakeAppStateStore());
        await service.LoadAsync();
        await service.CreateNoteAsync();

        var result = await service.AppendScannedCodesAsync(["SAME", "SAME"]);

        Assert.Equal(2, result.AppendedCount);
        Assert.Equal("SAME\nSAME", service.ActiveNote?.Content);
    }

    [Fact]
    public async Task AppendScannedCodesAsync_GroupSeparator_PreservesCharacterInNote()
    {
        var service = new AppStateService(new FakeAppStateStore());
        await service.LoadAsync();
        await service.CreateNoteAsync();

        await service.AppendScannedCodesAsync(["01\u001D21"]);

        Assert.Equal("01\u001D21", service.ActiveNote?.Content);
    }

    [Fact]
    public async Task AppendScannedCodesAsync_IgnoresEmptyCodes()
    {
        var store = new FakeAppStateStore();
        var service = new AppStateService(store);
        await service.LoadAsync();
        await service.CreateNoteAsync();
        var writesBeforeScan = store.ScheduledWriteCount;

        var result = await service.AppendScannedCodesAsync(["", string.Empty]);

        Assert.Equal(ScanAppendStatus.NoCodes, result.Status);
        Assert.Equal(0, result.AppendedCount);
        Assert.Equal(string.Empty, service.ActiveNote?.Content);
        Assert.Equal(writesBeforeScan, store.ScheduledWriteCount);
    }

    [Fact]
    public async Task AppendScannedCodesAsync_DoesNotAddExtraBlankLine()
    {
        var service = new AppStateService(new FakeAppStateStore());
        await service.LoadAsync();
        var note = await service.CreateNoteAsync();
        await service.UpdateNoteAsync(note.Id, string.Empty, "Первая строка\n");

        await service.AppendScannedCodesAsync(["CODE"]);

        Assert.Equal("Первая строка\nCODE", service.ActiveNote?.Content);
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

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
