namespace Dameview.Viewing;

internal abstract class WorkspaceNode;

internal enum WorkspaceSplitOrientation
{
    Horizontal,
    Vertical,
}

internal sealed class WorkspaceSplit : WorkspaceNode
{
    internal WorkspaceSplit(
        WorkspaceSplitOrientation orientation,
        WorkspaceNode first,
        WorkspaceNode second,
        float ratio = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (!(ratio > 0.0f && ratio < 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        Orientation = orientation;
        First = first;
        Second = second;
        Ratio = ratio;
    }

    internal WorkspaceSplitOrientation Orientation { get; }
    internal WorkspaceNode First { get; private set; }
    internal WorkspaceNode Second { get; private set; }
    internal float Ratio { get; private set; }

    internal void SetRatio(float ratio)
    {
        if (!(ratio > 0.0f && ratio < 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        Ratio = ratio;
    }

    internal bool ReplaceChild(WorkspaceNode child, WorkspaceNode replacement)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(replacement);
        if (ReferenceEquals(First, child))
        {
            First = replacement;
            return true;
        }

        if (ReferenceEquals(Second, child))
        {
            Second = replacement;
            return true;
        }

        return false;
    }

    internal WorkspaceNode GetSibling(WorkspaceNode child)
    {
        if (ReferenceEquals(First, child))
        {
            return Second;
        }

        if (ReferenceEquals(Second, child))
        {
            return First;
        }

        throw new ArgumentException("The node is not a child of this split.", nameof(child));
    }
}
