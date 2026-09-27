using System.Text;

namespace Dameview.Serialization;

// Small, deliberately limited INI document whose values are opaque strings.
// Comments and keys this version doesn't know survive a Parse and Write,
// while entries and headers are written back in a normalized form.
internal sealed class IniDocument
{
    // The first section holds the keys above any header and never has a header of its own.
    private readonly List<Section> _sections = [new Section(string.Empty)];

    internal static IniDocument Parse(string text)
    {
        var document = new IniDocument();
        Section section = document._sections[0];
        foreach (string rawLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                section.Lines.Add(new Comment(line));
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = new Section(line[1..^1].Trim());
                document._sections.Add(section);
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new IniFormatException("Expected a key=value line.");
            }

            section.Lines.Add(new Entry(line[..separator].Trim(), line[(separator + 1)..].Trim()));
        }

        // Write puts the blank line before each header itself.
        foreach (Section parsed in document._sections)
        {
            while (parsed.Lines.Count > 0 && parsed.Lines[^1].Text.Length == 0)
            {
                parsed.Lines.RemoveAt(parsed.Lines.Count - 1);
            }
        }

        return document;
    }

    internal string? Get(string section, string key)
    {
        return Find(section, key)?.Value;
    }

    internal void Set(string section, string key, string value)
    {
        if (Find(section, key) is Entry existing)
        {
            existing.Value = value;
            return;
        }

        Section? target = _sections.FindLast(candidate => candidate.Name == section);
        if (target is null)
        {
            target = new Section(section);
            _sections.Add(target);
        }

        int lastEntry = target.Lines.FindLastIndex(line => line is Entry);
        target.Lines.Insert(lastEntry < 0 ? target.Lines.Count : lastEntry + 1, new Entry(key, value));
    }

    internal bool HasSection(string section)
    {
        return _sections.FindIndex(1, candidate => candidate.Name == section) >= 0;
    }

    internal void RemoveSection(string section)
    {
        _sections.RemoveAll(candidate => candidate != _sections[0] && candidate.Name == section);
    }

    internal string Write()
    {
        var text = new StringBuilder(160);
        for (int index = 0; index < _sections.Count; index++)
        {
            Section section = _sections[index];
            if (index > 0)
            {
                if (text.Length > 0)
                {
                    text.AppendLine();
                }

                text.Append('[').Append(section.Name).AppendLine("]");
            }

            foreach (Line line in section.Lines)
            {
                text.AppendLine(line.Text);
            }
        }

        return text.ToString();
    }

    // A key repeated in the file resolves to its last occurrence, as if the lines were applied in order.
    private Entry? Find(string section, string key)
    {
        return _sections
            .Where(candidate => candidate.Name == section)
            .SelectMany(candidate => candidate.Lines)
            .OfType<Entry>()
            .LastOrDefault(entry => entry.Key == key);
    }

    private sealed class Section(string name)
    {
        internal string Name { get; } = name;
        internal List<Line> Lines { get; } = [];
    }

    private abstract class Line
    {
        internal abstract string Text { get; }
    }

    // Blank lines are kept as empty comments.
    private sealed class Comment(string text) : Line
    {
        internal override string Text { get; } = text;
    }

    private sealed class Entry(string key, string value) : Line
    {
        internal string Key { get; } = key;
        internal string Value { get; set; } = value;
        internal override string Text => $"{Key}={Value}";
    }
}

internal sealed class IniFormatException(string message) : Exception(message);
