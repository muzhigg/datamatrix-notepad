using Datamatrix_Notepad.Models;
using Microsoft.JSInterop;

namespace Datamatrix_Notepad.Services.Export;

public sealed record TextDownloadRequest(
    string FileName,
    string ContentType,
    string Content);

public interface IBrowserFileDownloader : IAsyncDisposable
{
    ValueTask DownloadAsync(TextDownloadRequest request);
}

public sealed class BrowserFileDownloader(IJSRuntime jsRuntime)
    : IBrowserFileDownloader
{
    private IJSObjectReference? _module;

    public async ValueTask DownloadAsync(TextDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContentType);
        ArgumentNullException.ThrowIfNull(request.Content);

        var module = await GetModuleAsync();
        await module.InvokeVoidAsync(
            "downloadText",
            request.FileName,
            request.ContentType,
            request.Content);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The browser is already unloading.
        }

        _module = null;
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./js/download.js");
        return _module;
    }
}

public sealed class NoteExportService(
    IExportFileNameService fileNameService,
    IBrowserFileDownloader fileDownloader)
{
    public string CreateFileName(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return fileNameService.CreateFileName(note.Title);
    }

    public async Task<string> DownloadAsync(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var fileName = CreateFileName(note);
        await fileDownloader.DownloadAsync(
            new TextDownloadRequest(
                fileName,
                "text/plain;charset=utf-8",
                note.Content));
        return fileName;
    }
}
