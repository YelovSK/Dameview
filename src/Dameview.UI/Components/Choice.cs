namespace Dameview.UI.Components;

/// <summary>One option of a control that picks a value, with the text it shows.</summary>
internal readonly record struct Choice<T>(string Label, T Value);
