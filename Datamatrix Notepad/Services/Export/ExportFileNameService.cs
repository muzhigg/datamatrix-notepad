using System.Globalization;
using System.Text.RegularExpressions;

namespace Datamatrix_Notepad.Services.Export;

public interface IExportFileNameService
{
    string CreateFileName(string noteTitle);
}

public sealed class ExportFileNameService : IExportFileNameService
{
    private static readonly Regex PartPattern = new(
        @"00ЦБ-([0-9]{6})(?![0-9])",
        RegexOptions.CultureInvariant);

    private static readonly Regex DatePattern = new(
        @"(?<![0-9])([0-9]{2}\.[0-9]{2}\.[0-9]{4})(?![0-9])",
        RegexOptions.CultureInvariant);

    public string CreateFileName(string noteTitle)
    {
        ArgumentNullException.ThrowIfNull(noteTitle);

        var partMatch = PartPattern.Match(noteTitle);
        var part1 = partMatch.Success
            ? partMatch.Groups[1].Value
            : string.Empty;
        var part2 = FindFirstValidDate(noteTitle);

        return $"{part1};{part2}.txt";
    }

    private static string FindFirstValidDate(string noteTitle)
    {
        foreach (Match match in DatePattern.Matches(noteTitle))
        {
            var candidate = match.Groups[1].Value;
            if (DateOnly.TryParseExact(
                    candidate,
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
