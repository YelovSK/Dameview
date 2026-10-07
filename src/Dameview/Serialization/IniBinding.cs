namespace Dameview.Serialization;

// Part of an INI file bound to part of an immutable model.
// Missing or unreadable values leave the model as it was, and unreadable ones are reported.
internal abstract class IniBinding<TModel>
{
    internal abstract TModel Read(IniDocument document, TModel model, List<string> ignored);

    internal abstract void Write(IniDocument document, TModel model);

    internal static IniKeyBinding<TModel, bool> Bool(
        string section,
        string key,
        Func<TModel, bool> get,
        Func<TModel, bool, TModel> set) =>
        new(section, key, IniValue.ParseBool, IniValue.FormatBool, get, set);

    internal static IniKeyBinding<TModel, DateTimeOffset> Time(
        string section,
        string key,
        Func<TModel, DateTimeOffset> get,
        Func<TModel, DateTimeOffset, TModel> set) =>
        new(section, key, IniValue.ParseTime, IniValue.FormatTime, get, set);

    internal static IniKeyBinding<TModel, T> Enum<T>(
        string section,
        string key,
        Func<TModel, T> get,
        Func<TModel, T, TModel> set)
        where T : struct, System.Enum =>
        new(section, key, IniValue.ParseEnum<T>, IniValue.FormatEnum, get, set);

    internal static IniKeyBinding<TModel, int> Int(
        string section,
        string key,
        int minimum,
        int maximum,
        Func<TModel, int> get,
        Func<TModel, int, TModel> set) =>
        new(
            section,
            key,
            text => IniValue.ParseInt(text) is int value && value >= minimum && value <= maximum ? value : null,
            IniValue.FormatInt,
            get,
            set);

    internal static IniKeyBinding<TModel, float> Float(
        string section,
        string key,
        float minimum,
        Func<TModel, float> get,
        Func<TModel, float, TModel> set,
        float maximum = float.MaxValue) =>
        new(
            section,
            key,
            text => IniValue.ParseFloat(text) is float value && value >= minimum && value <= maximum ? value : null,
            IniValue.FormatFloat,
            get,
            set);

    // For values that don't fit one key, like a whole section or a variable set of keys.
    internal static IniBinding<TModel> Custom(
        Func<IniDocument, TModel, List<string>, TModel> read,
        Action<IniDocument, TModel> write) =>
        new CustomBinding(read, write);

    private sealed class CustomBinding(
        Func<IniDocument, TModel, List<string>, TModel> read,
        Action<IniDocument, TModel> write) : IniBinding<TModel>
    {
        internal override TModel Read(IniDocument document, TModel model, List<string> ignored) =>
            read(document, model, ignored);

        internal override void Write(IniDocument document, TModel model) => write(document, model);
    }
}

// One key bound to one property.
internal sealed class IniKeyBinding<TModel, TValue>(
    string section,
    string key,
    Func<string, TValue?> parse,
    Func<TValue, string> format,
    Func<TModel, TValue> get,
    Func<TModel, TValue, TModel> set) : IniBinding<TModel>
    where TValue : struct
{
    internal override TModel Read(IniDocument document, TModel model, List<string> ignored)
    {
        if (document.Get(section, key) is not string text)
        {
            return model;
        }

        if (parse(text) is TValue value)
        {
            return set(model, value);
        }

        ignored.Add($"{(section.Length == 0 ? key : $"{section}.{key}")} '{text}'");
        return model;
    }

    internal override void Write(IniDocument document, TModel model)
    {
        document.Set(section, key, format(get(model)));
    }
}
