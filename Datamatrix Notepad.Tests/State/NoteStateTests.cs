using Datamatrix_Notepad.Services.State;
using Xunit;

namespace Datamatrix_Notepad.Tests.State;

public sealed class NoteStateTests
{
    [Fact]
    public async Task CreateNoteAsync_CreatesActiveNoteAndSavesImmediately()
    {
        var store = new FakeAppStateStore();
        var timeProvider = new TestTimeProvider(
            DateTimeOffset.Parse("2026-07-25T10:00:00Z"));
        var service = new AppStateService(store, timeProvider);
        await service.LoadAsync();

        var note = await service.CreateNoteAsync();

        Assert.NotEqual(Guid.Empty, note.Id);
        Assert.Equal(string.Empty, note.Title);
        Assert.Equal(note.Id, service.State.ActiveNoteId);
        Assert.Equal(note, Assert.Single(service.State.Notes));
        Assert.Equal(1, store.ImmediateWriteCount);
    }

    [Fact]
    public async Task UpdateNoteAsync_UpdatesFieldsAndMovesNoteToTop()
    {
        var store = new FakeAppStateStore();
        var timeProvider = new TestTimeProvider(
            DateTimeOffset.Parse("2026-07-25T10:00:00Z"));
        var service = new AppStateService(store, timeProvider);
        await service.LoadAsync();
        var firstNote = await service.CreateNoteAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await service.CreateNoteAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await service.UpdateNoteAsync(firstNote.Id, "Партия", "CODE");

        var updated = service.State.Notes[0];
        Assert.Equal(firstNote.Id, updated.Id);
        Assert.Equal("Партия", updated.Title);
        Assert.Equal("CODE", updated.Content);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-25T10:02:00Z"),
            updated.UpdatedAtUtc);
        Assert.Equal(1, store.ScheduledWriteCount);
    }

    [Fact]
    public async Task SelectNoteAsync_ChangesActiveNoteWithoutChangingTimestamp()
    {
        var store = new FakeAppStateStore();
        var timeProvider = new TestTimeProvider(
            DateTimeOffset.Parse("2026-07-25T10:00:00Z"));
        var service = new AppStateService(store, timeProvider);
        await service.LoadAsync();
        var firstNote = await service.CreateNoteAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var secondNote = await service.CreateNoteAsync();
        var originalTimestamp = firstNote.UpdatedAtUtc;

        await service.SelectNoteAsync(firstNote.Id);

        Assert.Equal(firstNote.Id, service.State.ActiveNoteId);
        Assert.Equal(
            originalTimestamp,
            service.State.Notes.Single(note => note.Id == firstNote.Id).UpdatedAtUtc);
        Assert.Equal(secondNote.Id, service.State.Notes[0].Id);
        Assert.Equal(1, store.ScheduledWriteCount);
    }

    [Fact]
    public async Task UpdateNoteAsync_RejectsUnknownNote()
    {
        var service = new AppStateService(
            new FakeAppStateStore(),
            new TestTimeProvider(DateTimeOffset.UtcNow));
        await service.LoadAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.UpdateNoteAsync(Guid.NewGuid(), "Заголовок", "Текст"));
    }

    [Fact]
    public async Task DeleteNoteAsync_DeletesActiveNoteAndSelectsNextNote()
    {
        var store = new FakeAppStateStore();
        var timeProvider = new TestTimeProvider(
            DateTimeOffset.Parse("2026-07-25T10:00:00Z"));
        var service = new AppStateService(store, timeProvider);
        await service.LoadAsync();
        var firstNote = await service.CreateNoteAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var secondNote = await service.CreateNoteAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await service.CreateNoteAsync();
        await service.SelectNoteAsync(secondNote.Id);
        var writesBeforeDeletion = store.ImmediateWriteCount;

        await service.DeleteNoteAsync(secondNote.Id);

        Assert.DoesNotContain(
            service.State.Notes,
            note => note.Id == secondNote.Id);
        Assert.Equal(firstNote.Id, service.State.ActiveNoteId);
        Assert.Equal(writesBeforeDeletion + 1, store.ImmediateWriteCount);
    }

    [Fact]
    public async Task DeleteNoteAsync_DeletesLastNoteAndClearsSelection()
    {
        var store = new FakeAppStateStore();
        var service = new AppStateService(
            store,
            new TestTimeProvider(DateTimeOffset.UtcNow));
        await service.LoadAsync();
        var note = await service.CreateNoteAsync();

        await service.DeleteNoteAsync(note.Id);

        Assert.Empty(service.State.Notes);
        Assert.Null(service.State.ActiveNoteId);
        Assert.Null(service.ActiveNote);
        Assert.Equal(2, store.ImmediateWriteCount);
    }

    [Fact]
    public async Task DeleteNoteAsync_RejectsUnknownNote()
    {
        var service = new AppStateService(
            new FakeAppStateStore(),
            new TestTimeProvider(DateTimeOffset.UtcNow));
        await service.LoadAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.DeleteNoteAsync(Guid.NewGuid()));
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
