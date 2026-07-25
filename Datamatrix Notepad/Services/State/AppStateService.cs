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

public sealed class AppStateService(IAppStateStore store)
{
    public const string StorageKey = "datamatrix-notepad.state.v1";
    public const int MaximumSerializedStateLength = 4_500_000;
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(300);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private StateLoadResult? _loadResult;

    public AppState State { get; private set; } = AppState.CreateDefault();

    public bool IsLoaded => _loadResult is not null;

    public StatePersistenceStatus PersistenceStatus { get; private set; } =
        StatePersistenceStatus.Saved;

    public string? PersistenceError { get; private set; }

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
            storedValue = await store.ReadAsync(StorageKey);
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
                await store.WriteNowAsync(StorageKey, serializedState);
            }
            else
            {
                await store.ScheduleWriteAsync(
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
}
