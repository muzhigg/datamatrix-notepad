using Datamatrix_Notepad.Models;
using Datamatrix_Notepad.Services.Export;
using Xunit;

namespace Datamatrix_Notepad.Tests.Export;

public sealed class NoteExportServiceTests
{
    [Fact]
    public async Task DownloadAsync_UsesComputedNameUtf8TextMimeAndExactContent()
    {
        var downloader = new RecordingBrowserFileDownloader();
        var service = new NoteExportService(
            new ExportFileNameService(),
            downloader);
        var note = new Note(
            Guid.NewGuid(),
            "Партия 00ЦБ-123456 от 25.07.2026",
            "строка 1\nCODE\u001D✓",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var fileName = await service.DownloadAsync(note);

        Assert.Equal("123456;25.07.2026.txt", fileName);
        var request = Assert.Single(downloader.Requests);
        Assert.Equal(fileName, request.FileName);
        Assert.Equal("text/plain;charset=utf-8", request.ContentType);
        Assert.Equal("строка 1\nCODE\u001D✓", request.Content);
    }

    [Fact]
    public async Task DownloadAsync_DoesNotMutateNote()
    {
        var downloader = new RecordingBrowserFileDownloader();
        var service = new NoteExportService(
            new ExportFileNameService(),
            downloader);
        var note = new Note(
            Guid.NewGuid(),
            string.Empty,
            "CODE",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await service.DownloadAsync(note);

        Assert.Equal(string.Empty, note.Title);
        Assert.Equal("CODE", note.Content);
        Assert.Equal(";.txt", downloader.Requests.Single().FileName);
    }

    [Fact]
    public async Task DownloadAsync_RejectsNullNote()
    {
        var service = new NoteExportService(
            new ExportFileNameService(),
            new RecordingBrowserFileDownloader());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.DownloadAsync(null!));
    }

    private sealed class RecordingBrowserFileDownloader : IBrowserFileDownloader
    {
        public List<TextDownloadRequest> Requests { get; } = [];

        public ValueTask DownloadAsync(TextDownloadRequest request)
        {
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
