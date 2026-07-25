using System.Text;
using Microsoft.JSInterop;

namespace Datamatrix_Notepad.Services.Serial;

public enum SerialConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public sealed record SerialConnectionOptions(
    int BaudRate,
    int DataBits,
    string Parity,
    int StopBits,
    string FlowControl,
    string Prefix,
    string Suffix)
{
    public const int MaximumDelimiterLength = 100;

    public static SerialConnectionOptions Default { get; } = new(
        BaudRate: 9600,
        DataBits: 8,
        Parity: "none",
        StopBits: 1,
        FlowControl: "none",
        Prefix: string.Empty,
        Suffix: "\r\n");

    public void Validate()
    {
        if (BaudRate <= 0)
        {
            throw new ArgumentException("Baud rate must be positive.", nameof(BaudRate));
        }

        if (DataBits is not (7 or 8))
        {
            throw new ArgumentException("Data bits must be 7 or 8.", nameof(DataBits));
        }

        if (Parity is not ("none" or "even" or "odd"))
        {
            throw new ArgumentException("Parity must be none, even, or odd.", nameof(Parity));
        }

        if (StopBits is not (1 or 2))
        {
            throw new ArgumentException("Stop bits must be 1 or 2.", nameof(StopBits));
        }

        if (FlowControl is not ("none" or "hardware"))
        {
            throw new ArgumentException("Flow control must be none or hardware.", nameof(FlowControl));
        }

        if (Prefix is null)
        {
            throw new ArgumentNullException(nameof(Prefix));
        }

        if (string.IsNullOrEmpty(Suffix))
        {
            throw new ArgumentException("Suffix must not be empty.", nameof(Suffix));
        }

        if (Prefix.Length > MaximumDelimiterLength ||
            Suffix.Length > MaximumDelimiterLength)
        {
            throw new ArgumentException(
                $"Prefix and suffix must not exceed {MaximumDelimiterLength} characters.");
        }
    }
}

public sealed record SerialPortInfo(ushort? UsbVendorId, ushort? UsbProductId)
{
    public string DisplayName =>
        UsbVendorId is not null && UsbProductId is not null
            ? $"VID 0x{UsbVendorId:X4} / PID 0x{UsbProductId:X4}"
            : "Выбранный порт";
}

public sealed record SerialConnectResult(
    bool IsConnected,
    bool IsCancelled,
    SerialPortInfo? Port,
    string? ErrorMessage);

public enum SerialReconnectStatus
{
    Connected,
    NotFound,
    Ambiguous,
    Error
}

public sealed record SerialReconnectResult(
    SerialReconnectStatus Status,
    SerialPortInfo? Port,
    string? ErrorMessage)
{
    public bool IsConnected => Status == SerialReconnectStatus.Connected;
}

public sealed record SerialChunkEventArgs(
    string RawText,
    IReadOnlyList<string> CompletedCodes);

public static class SerialTextFormatter
{
    public static string MakeControlCharactersVisible(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    public static bool TryParseControlCharacters(
        string displayValue,
        out string value)
    {
        ArgumentNullException.ThrowIfNull(displayValue);

        var builder = new StringBuilder(displayValue.Length);
        for (var index = 0; index < displayValue.Length; index++)
        {
            var character = displayValue[index];
            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            if (++index >= displayValue.Length)
            {
                value = string.Empty;
                return false;
            }

            switch (displayValue[index])
            {
                case '\\':
                    builder.Append('\\');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                default:
                    value = string.Empty;
                    return false;
            }
        }

        value = builder.ToString();
        return true;
    }
}

public sealed class SerialPortService(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IJSObjectReference? _module;
    private DotNetObjectReference<SerialPortService>? _selfReference;
    private ScanFrameParser? _frameParser;
    private bool _disposed;

    public SerialConnectionState State { get; private set; } = SerialConnectionState.Disconnected;

    public SerialPortInfo? SelectedPort { get; private set; }

    public string? ErrorMessage { get; private set; }

    public event EventHandler? StateChanged;

    public event EventHandler<SerialChunkEventArgs>? ChunkReceived;

    public event EventHandler<string>? DiagnosticErrorReceived;

    public async ValueTask<bool> IsSupportedAsync()
    {
        ThrowIfDisposed();
        var module = await GetModuleAsync();
        return await module.InvokeAsync<bool>("isSupported");
    }

    public async Task<SerialConnectResult> SelectAndConnectAsync(SerialConnectionOptions options)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        await _connectionLock.WaitAsync();
        try
        {
            if (State == SerialConnectionState.Connected)
            {
                await DisconnectCoreAsync();
            }

            SetState(SerialConnectionState.Connecting);
            _frameParser = new ScanFrameParser(options.Prefix, options.Suffix);
            _selfReference ??= DotNetObjectReference.Create(this);

            var module = await GetModuleAsync();
            var selection = await module.InvokeAsync<BrowserPortSelection>(
                "requestAndOpen",
                options,
                _selfReference);

            if (selection.Cancelled)
            {
                SetState(SerialConnectionState.Disconnected);
                return new SerialConnectResult(false, true, null, null);
            }

            SelectedPort = new SerialPortInfo(
                ToUShort(selection.UsbVendorId),
                ToUShort(selection.UsbProductId));
            SetState(SerialConnectionState.Connected);

            return new SerialConnectResult(true, false, SelectedPort, null);
        }
        catch (JSException exception)
        {
            var errorMessage = $"Не удалось подключить порт: {exception.Message}";
            SetState(SerialConnectionState.Error, errorMessage);
            return new SerialConnectResult(false, false, null, errorMessage);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<SerialReconnectResult> TryReconnectAsync(
        SerialConnectionOptions options,
        SerialPortInfo? expectedPort)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        await _connectionLock.WaitAsync();
        try
        {
            if (State == SerialConnectionState.Connected &&
                SelectedPort is not null)
            {
                return new SerialReconnectResult(
                    SerialReconnectStatus.Connected,
                    SelectedPort,
                    null);
            }

            SetState(SerialConnectionState.Connecting);
            _frameParser = new ScanFrameParser(options.Prefix, options.Suffix);
            _selfReference ??= DotNetObjectReference.Create(this);

            var module = await GetModuleAsync();
            var reconnectResult = await module.InvokeAsync<BrowserReconnectResult>(
                "openPreviouslyGranted",
                options,
                expectedPort?.UsbVendorId,
                expectedPort?.UsbProductId,
                _selfReference);

            if (reconnectResult.Status == "notFound")
            {
                _frameParser = null;
                SetState(SerialConnectionState.Disconnected);
                return new SerialReconnectResult(
                    SerialReconnectStatus.NotFound,
                    null,
                    null);
            }

            if (reconnectResult.Status == "ambiguous")
            {
                _frameParser = null;
                SetState(SerialConnectionState.Disconnected);
                return new SerialReconnectResult(
                    SerialReconnectStatus.Ambiguous,
                    null,
                    null);
            }

            if (reconnectResult.Status != "connected")
            {
                throw new JSException("Браузер вернул неизвестный результат подключения.");
            }

            SelectedPort = new SerialPortInfo(
                ToUShort(reconnectResult.UsbVendorId),
                ToUShort(reconnectResult.UsbProductId));
            SetState(SerialConnectionState.Connected);
            return new SerialReconnectResult(
                SerialReconnectStatus.Connected,
                SelectedPort,
                null);
        }
        catch (JSException exception)
        {
            var errorMessage =
                $"Не удалось повторно подключить разрешённый порт: {exception.Message}";
            SetState(SerialConnectionState.Error, errorMessage);
            return new SerialReconnectResult(
                SerialReconnectStatus.Error,
                null,
                errorMessage);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();

        await _connectionLock.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
        }
        catch (JSException exception)
        {
            SetState(
                SerialConnectionState.Error,
                $"Не удалось закрыть порт: {exception.Message}");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    [JSInvokable]
    public Task ReceiveChunkAsync(string chunk)
    {
        if (_disposed || _frameParser is null || string.IsNullOrEmpty(chunk))
        {
            return Task.CompletedTask;
        }

        var completedCodes = _frameParser.Append(chunk);
        ChunkReceived?.Invoke(this, new SerialChunkEventArgs(chunk, completedCodes));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task ReportReadErrorAsync(string message)
    {
        if (!_disposed)
        {
            DiagnosticErrorReceived?.Invoke(this, message);
        }

        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task NotifyDisconnectedAsync()
    {
        if (!_disposed)
        {
            _frameParser?.Reset();
            SetState(SerialConnectionState.Disconnected);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _connectionLock.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
            _selfReference?.Dispose();
            _selfReference = null;

            if (_module is not null)
            {
                await _module.DisposeAsync();
                _module = null;
            }

            _disposed = true;
        }
        catch (JSException)
        {
            _disposed = true;
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        // Blazor recommends isolated ES modules loaded with the special import identifier.
        // https://learn.microsoft.com/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-10.0#javascript-isolation-in-javascript-modules
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./js/serial.js");
        return _module;
    }

    private async Task DisconnectCoreAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("close");
        }

        _frameParser?.Reset();
        SelectedPort = null;
        SetState(SerialConnectionState.Disconnected);
    }

    private void SetState(SerialConnectionState state, string? errorMessage = null)
    {
        State = state;
        ErrorMessage = errorMessage;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static ushort? ToUShort(int? value) =>
        value is >= ushort.MinValue and <= ushort.MaxValue
            ? (ushort)value.Value
            : null;

    private sealed record BrowserPortSelection(
        bool Cancelled,
        int? UsbVendorId,
        int? UsbProductId);

    private sealed record BrowserReconnectResult(
        string Status,
        int? UsbVendorId,
        int? UsbProductId);
}
