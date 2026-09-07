using System.Text;

namespace Dameview.Serialization;

// Small, deliberately limited INI document. Values are opaque strings.
internal sealed class IniDocument
{
    private readonly List<Entry> _entries = new();
    private readonly List<string> _sections = new();

    internal static IniDocument Parse(string text)
    {
        var document = new IniDocument();
        string section = string.Empty;
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                document.AddSection(section);
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new IniFormatException("Expected a key=value line.");
            }

            document.Set(section, line[..separator].Trim(), line[(separator + 1)..].Trim());
        }

        return document;
    }

    internal string? Get(string section, string key)
    {
        for (int index = 0; index < _entries.Count; index++)
        {
            Entry entry = _entries[index];
            if (entry.Section == section && entry.Key == key)
            {
                return entry.Value;
            }
        }

        return null;
    }

    internal void Set(string section, string key, string value)
    {
        AddSection(section);
        for (int index = 0; index < _entries.Count; index++)
        {
            Entry entry = _entries[index];
            if (entry.Section == section && entry.Key == key)
            {
                _entries[index] = new Entry(section, key, value);
                return;
            }
        }

        _entries.Add(new Entry(section, key, value));
    }

    internal bool HasSection(string section)
    {
        return _sections.Contains(section, StringComparer.Ordinal);
    }

    internal string Write()
    {
        var text = new StringBuilder(160);
        string? section = null;
        foreach (Entry entry in _entries)
        {
            if (section is not null && section != entry.Section)
            {
                text.AppendLine();
            }

            if (section != entry.Section)
            {
                section = entry.Section;
                if (section.Length > 0)
                {
                    text.Append('[').Append(section).AppendLine("]");
                }
            }

            text.Append(entry.Key).Append('=').AppendLine(entry.Value);
        }

        return text.ToString();
    }

    private void AddSection(string section)
    {
        if (section.Length > 0 && !_sections.Contains(section, StringComparer.Ordinal))
        {
            _sections.Add(section);
        }
    }

    private readonly record struct Entry(string Section, string Key, string Value);
}

internal sealed class IniFormatException(string message) : Exception(message);
