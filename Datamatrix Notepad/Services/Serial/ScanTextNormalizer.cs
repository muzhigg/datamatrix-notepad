namespace Datamatrix_Notepad.Services.Serial;

public static class ScanTextNormalizer
{
    public const string GroupSeparator = "\u001D";

    public static string NormalizeCompletedCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Replace(
            GroupSeparator,
            string.Empty,
            StringComparison.Ordinal);
    }
}
