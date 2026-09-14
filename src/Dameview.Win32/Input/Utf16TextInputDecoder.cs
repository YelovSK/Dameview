namespace Dameview.Platform;

internal sealed class Utf16TextInputDecoder
{
    private char? _highSurrogate;

    internal string? Push(char codeUnit)
    {
        if (char.IsControl(codeUnit))
        {
            _highSurrogate = null;
            return null;
        }

        if (char.IsHighSurrogate(codeUnit))
        {
            string? replacement = _highSurrogate is null ? null : "\uFFFD";
            _highSurrogate = codeUnit;
            return replacement;
        }

        if (char.IsLowSurrogate(codeUnit))
        {
            if (_highSurrogate is not char highSurrogate)
            {
                return null;
            }

            _highSurrogate = null;
            return string.Create(2, (highSurrogate, codeUnit), static (buffer, pair) =>
            {
                buffer[0] = pair.highSurrogate;
                buffer[1] = pair.codeUnit;
            });
        }

        string text = _highSurrogate is null ? codeUnit.ToString() : $"\uFFFD{codeUnit}";
        _highSurrogate = null;
        return text;
    }
}
