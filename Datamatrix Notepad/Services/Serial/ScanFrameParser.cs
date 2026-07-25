namespace Datamatrix_Notepad.Services.Serial;

public sealed class ScanFrameParser
{
    private readonly string _prefix;
    private readonly string _suffix;
    private string _buffer = string.Empty;
    private bool _isInsideFrame;

    public ScanFrameParser(string prefix, string suffix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentException.ThrowIfNullOrEmpty(suffix);

        _prefix = prefix;
        _suffix = suffix;
        _isInsideFrame = prefix.Length == 0;
    }

    public IReadOnlyList<string> Append(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (chunk.Length == 0)
        {
            return [];
        }

        _buffer += chunk;
        var completedCodes = new List<string>();

        while (TryStartFrame())
        {
            var suffixIndex = _buffer.IndexOf(_suffix, StringComparison.Ordinal);
            if (suffixIndex < 0)
            {
                break;
            }

            var code = _buffer[..suffixIndex];
            _buffer = _buffer[(suffixIndex + _suffix.Length)..];

            if (code.Length > 0)
            {
                completedCodes.Add(code);
            }

            _isInsideFrame = _prefix.Length == 0;
        }

        return completedCodes;
    }

    public void Reset()
    {
        _buffer = string.Empty;
        _isInsideFrame = _prefix.Length == 0;
    }

    private bool TryStartFrame()
    {
        if (_isInsideFrame)
        {
            return true;
        }

        var prefixIndex = _buffer.IndexOf(_prefix, StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            PreservePossiblePrefixStart();
            return false;
        }

        _buffer = _buffer[(prefixIndex + _prefix.Length)..];
        _isInsideFrame = true;
        return true;
    }

    private void PreservePossiblePrefixStart()
    {
        var maximumLength = Math.Min(_buffer.Length, _prefix.Length - 1);

        for (var length = maximumLength; length > 0; length--)
        {
            var candidate = _buffer[^length..];
            if (_prefix.StartsWith(candidate, StringComparison.Ordinal))
            {
                _buffer = candidate;
                return;
            }
        }

        _buffer = string.Empty;
    }
}
