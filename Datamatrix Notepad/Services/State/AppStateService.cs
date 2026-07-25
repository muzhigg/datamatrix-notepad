using System.Text.Json;
using Datamatrix_Notepad.Models;
using Microsoft.JSInterop;

namespace Datamatrix_Notepad.Services.State;

public enum StatePersistenceStatus
{
    Saved,
    Saving,
    Error
}

public sealed record StateLoadResult(bool IsSuccess, string? ErrorMessage);

public sealed record StateSaveResult(bool IsSuccess, string? ErrorMessage);

public interface IAppStateStore : IAsyncDisposable
{
    ValueTask<string?> ReadAsync(string key);

    ValueTask ScheduleWriteAsync(string key, string value, TimeSpan debounce);

    ValueTask WriteNowAsync(string key, string value);
}

public sealed class BrowserAppStateStore(IJSRuntime jsRuntime) : IAppStateStore
{
    private IJSObjectReference? _module;

    public async ValueTask<string?> ReadAsync(string key)
    {
        var module = await GetModuleAsync();
        return await module.InvokeAsync<string?>("read", key);
    }

    public async ValueTask ScheduleWriteAsync(
        string key,
        string value,
        TimeSpan debounce)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync(
            "scheduleWrite",
            key,
            value,
            debounce.TotalMilliseconds);
    }

    public async ValueTask WriteNowAsync(string key, string value)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("writeNow", key, value);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
        catch (JSException)
        {
            // The browser may already be unloading; pagehide flush runs in JavaScript.
        }

        _module = null;
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        // Use an isolated ES module instead of global functions.
        // https://learn.microsoft.com/aspnet/core/blazor/javascript-interoperability/location-of-javascript?view=aspnetcore-10.0#javascript-isolation-in-javascript-modules
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./js/storage.js");
        return _module;
    }
}

public sealed class AppStateService
{
    public const string StorageKey = "datamatrix-notepad.state.v1";
    public const int MaximumSerializedStateLength = 4_500_000;
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(300);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IAppStateStore _store;
    private readonly TimeProvider _timeProvider;
    private StateLoadResult? _loadResult;

    public AppStateService(IAppStateStore store)
        : this(store, TimeProvider.System)
    {
    }

    public AppStateService(IAppStateStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    public AppState State { get; private set; } = AppState.CreateDefault();

    public bool IsLoaded => _loadResult is not null;

    public StatePersistenceStatus PersistenceStatus { get; private set; } =
        StatePersistenceStatus.Saved;

    public string? PersistenceError { get; private set; }

    public Note? ActiveNote =>
        State.ActiveNoteId is null
            ? null
            : State.Notes.FirstOrDefault(note => note.Id == State.ActiveNoteId.Value);

    public event EventHandler? StateChanged;

    public async Task<StateLoadResult> LoadAsync()
    {
        if (_loadResult is not null)
        {
            return _loadResult;
        }

        string? storedValue;
        try
        {
            storedValue = await _store.ReadAsync(StorageKey);
        }
        catch
        {
            return CompleteFailedLoad("Не удалось прочитать данные браузера.");
        }

        if (string.IsNullOrWhiteSpace(storedValue))
        {
            _loadResult = new StateLoadResult(true, null);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return _loadResult;
        }

        if (storedValue.Length > MaximumSerializedStateLength)
        {
            return CompleteFailedLoad("Сохранённые данные превышают допустимый размер.");
        }

        try
        {
            var restoredState = JsonSerializer.Deserialize<AppState>(
                storedValue,
                JsonOptions);

            if (restoredState is null)
            {
                return CompleteFailedLoad("Сохранённые данные пусты.");
            }

            restoredState.Validate();
            State = restoredState;
            _loadResult = new StateLoadResult(true, null);
            PersistenceStatus = StatePersistenceStatus.Saved;
            PersistenceError = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return _loadResult;
        }
        catch (JsonException)
        {
            return CompleteFailedLoad("Сохранённые данные имеют неверный формат.");
        }
        catch (InvalidDataException)
        {
            return CompleteFailedLoad("Сохранённые данные несовместимы с приложением.");
        }
        catch (ArgumentException)
        {
            return CompleteFailedLoad("Сохранённые настройки содержат недопустимые значения.");
        }
    }

    public Task<StateSaveResult> ScheduleSaveAsync() =>
        SaveAsync(immediate: false);

    public Task<StateSaveResult> SaveNowAsync() =>
        SaveAsync(immediate: true);

    public async Task<Note> CreateNoteAsync()
    {
        EnsureLoaded();

        var now = _timeProvider.GetUtcNow();
        var note = new Note(
            Guid.NewGuid(),
            Title: string.Empty,
            Content: string.Empty,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        State = State with
        {
            ActiveNoteId = note.Id,
            Notes = [note, .. State.Notes]
        };
        StateChanged?.Invoke(this, EventArgs.Empty);
        await SaveNowAsync();
        return note;
    }

    public async Task SelectNoteAsync(Guid noteId)
    {
        EnsureLoaded();

        if (State.Notes.All(note => note.Id != noteId))
        {
            throw new KeyNotFoundException($"Note {noteId} does not exist.");
        }

        if (State.ActiveNoteId == noteId)
        {
            return;
        }

        State = State with { ActiveNoteId = noteId };
        StateChanged?.Invoke(this, EventArgs.Empty);
        await ScheduleSaveAsync();
    }

    public async Task UpdateNoteAsync(
        Guid noteId,
        string title,
        string content)
    {
        EnsureLoaded();
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(content);

        if (title.Length > AppState.MaximumTitleLength)
        {
            throw new ArgumentException("Note title is too long.", nameof(title));
        }

        if (content.Length > AppState.MaximumContentLength)
        {
            throw new ArgumentException("Note content is too long.", nameof(content));
        }

        var existingNote = State.Notes.FirstOrDefault(note => note.Id == noteId)
            ?? throw new KeyNotFoundException($"Note {noteId} does not exist.");
        var updatedNote = existingNote with
        {
            Title = title,
            Content = content,
            UpdatedAtUtc = _timeProvider.GetUtcNow()
        };

        State = State with
        {
            Notes = State.Notes
                .Select(note => note.Id == noteId ? updatedNote : note)
                .OrderByDescending(note => note.UpdatedAtUtc)
                .ToArray()
        };
        StateChanged?.Invoke(this, EventArgs.Empty);
        await ScheduleSaveAsync();
    }

    private async Task<StateSaveResult> SaveAsync(bool immediate)
    {
        State.Validate();
        PersistenceStatus = StatePersistenceStatus.Saving;
        PersistenceError = null;
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var serializedState = JsonSerializer.Serialize(State, JsonOptions);
            if (serializedState.Length > MaximumSerializedStateLength)
            {
                return CompleteFailedSave("Данные не помещаются в браузерное хранилище.");
            }

            if (immediate)
            {
                await _store.WriteNowAsync(StorageKey, serializedState);
            }
            else
            {
                await _store.ScheduleWriteAsync(
                    StorageKey,
                    serializedState,
                    SaveDebounce);
            }

            PersistenceStatus = StatePersistenceStatus.Saved;
            PersistenceError = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new StateSaveResult(true, null);
        }
        catch
        {
            return CompleteFailedSave("Не удалось сохранить данные в браузере.");
        }
    }

    private StateLoadResult CompleteFailedLoad(string message)
    {
        _loadResult = new StateLoadResult(false, message);
        PersistenceStatus = StatePersistenceStatus.Error;
        PersistenceError = message;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return _loadResult;
    }

    private StateSaveResult CompleteFailedSave(string message)
    {
        PersistenceStatus = StatePersistenceStatus.Error;
        PersistenceError = message;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return new StateSaveResult(false, message);
    }

    private void EnsureLoaded()
    {
        if (!IsLoaded)
        {
            throw new InvalidOperationException(
                "Application state must be loaded before it can be changed.");
        }
    }
}
