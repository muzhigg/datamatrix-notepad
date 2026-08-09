using Datamatrix_Notepad.Services.Serial;

namespace Datamatrix_Notepad.Models;

public sealed record Note(
    Guid Id,
    string Title,
    string Content,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SerialSettings(
    string PortAlias,
    ushort? UsbVendorId,
    ushort? UsbProductId,
    int BaudRate,
    int DataBits,
    string Parity,
    int StopBits,
    string FlowControl,
    string Encoding,
    string Prefix,
    string Suffix)
{
    public static SerialSettings Default { get; } = new(
        PortAlias: "Сканер",
        UsbVendorId: null,
        UsbProductId: null,
        BaudRate: 9600,
        DataBits: 8,
        Parity: "none",
        StopBits: 1,
        FlowControl: "none",
        Encoding: SerialConnectionOptions.Utf8Encoding,
        Prefix: string.Empty,
        Suffix: "\r\n");

    public SerialConnectionOptions ToConnectionOptions() =>
        new(
            BaudRate,
            DataBits,
            Parity,
            StopBits,
            FlowControl,
            Encoding,
            Prefix,
            Suffix);

    public void Validate()
    {
        if (PortAlias is null)
        {
            throw new InvalidDataException("Port alias is required.");
        }

        if (PortAlias.Length > 200)
        {
            throw new InvalidDataException("Port alias is too long.");
        }

        if (!SerialConnectionOptions.IsSupportedEncoding(Encoding))
        {
            throw new InvalidDataException("Only UTF-8 and UTF-16BE encodings are supported.");
        }

        ToConnectionOptions().Validate();
    }
}

public sealed record AppState(
    int SchemaVersion,
    Guid? ActiveNoteId,
    IReadOnlyList<Note> Notes,
    SerialSettings SerialSettings)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumNoteCount = 10_000;
    public const int MaximumTitleLength = 10_000;
    public const int MaximumContentLength = 4_000_000;

    public static AppState CreateDefault() =>
        new(
            CurrentSchemaVersion,
            ActiveNoteId: null,
            Notes: [],
            SerialSettings.Default);

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported state schema version: {SchemaVersion}.");
        }

        if (Notes is null)
        {
            throw new InvalidDataException("Notes collection is required.");
        }

        if (Notes.Count > MaximumNoteCount)
        {
            throw new InvalidDataException("Too many notes in saved state.");
        }

        var noteIds = new HashSet<Guid>();
        long totalContentLength = 0;

        foreach (var note in Notes)
        {
            if (note is null)
            {
                throw new InvalidDataException("A saved note is null.");
            }

            if (note.Id == Guid.Empty || !noteIds.Add(note.Id))
            {
                throw new InvalidDataException("Note identifiers must be unique and non-empty.");
            }

            if (note.Title is null || note.Content is null)
            {
                throw new InvalidDataException("Note text fields are required.");
            }

            if (note.Title.Length > MaximumTitleLength)
            {
                throw new InvalidDataException("A note title is too long.");
            }

            totalContentLength += note.Content.Length;
            if (totalContentLength > MaximumContentLength)
            {
                throw new InvalidDataException("Saved note content is too large.");
            }
        }

        if (ActiveNoteId is not null && !noteIds.Contains(ActiveNoteId.Value))
        {
            throw new InvalidDataException("Active note does not exist.");
        }

        if (SerialSettings is null)
        {
            throw new InvalidDataException("Serial settings are required.");
        }

        SerialSettings.Validate();
    }
}
