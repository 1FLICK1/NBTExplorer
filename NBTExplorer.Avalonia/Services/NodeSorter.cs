using NBTExplorer.Model;
using NBTExplorer.Utility;
using Substrate.Nbt;

namespace NBTExplorer.Avalonia.Services;

/// <summary>
/// Port of NodeTreeComparer (Controllers\NodeTreeController.cs:25-104), with the WinForms
/// TreeNode unwrapping removed. Avalonia's TreeView has no equivalent of TreeViewNodeSorter,
/// so sorting is applied explicitly when children are synced.
/// </summary>
public sealed class NodeSorter : IComparer<DataNode>
{
    private readonly NaturalComparer _natural = new();

    /// <summary>Compounds, then lists, then scalars, then everything else.</summary>
    private static int OrderForTag(TagType type) => type switch {
        TagType.TAG_COMPOUND => 0,
        TagType.TAG_LIST => 1,
        TagType.TAG_BYTE or TagType.TAG_SHORT or TagType.TAG_INT or TagType.TAG_LONG
            or TagType.TAG_FLOAT or TagType.TAG_DOUBLE or TagType.TAG_STRING => 2,
        _ => 3,
    };

    /// <summary>Directories sort above files, matching Explorer.</summary>
    private static int OrderForNode(DataNode node) => node is DirectoryDataNode ? 0 : 1;

    public int Compare(DataNode? x, DataNode? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        if (x is not TagDataNode tx || y is not TagDataNode ty) {
            int nodeOrder = OrderForNode(x).CompareTo(OrderForNode(y));
            return nodeOrder != 0 ? nodeOrder : _natural.Compare(x.NodeDisplay, y.NodeDisplay);
        }

        // TAG_LIST order is semantically meaningful — the index IS the data. Returning 0 leaves
        // the children in insertion order. Dropping this makes the app silently reorder lists.
        if (tx.Parent is TagDataNode px && ty.Parent is TagDataNode py) {
            if (px.Tag.GetTagType() == TagType.TAG_LIST || py.Tag.GetTagType() == TagType.TAG_LIST)
                return 0;
        }

        int tagOrder = OrderForTag(tx.Tag.GetTagType()).CompareTo(OrderForTag(ty.Tag.GetTagType()));
        return tagOrder != 0 ? tagOrder : _natural.Compare(tx.NodeDisplay, ty.NodeDisplay);
    }

    /// <summary>
    /// Stable sort that respects the TAG_LIST "return 0" rule. List.Sort is an unstable
    /// introsort, so a comparer that returns 0 for list children could still shuffle them.
    /// </summary>
    public void Sort(List<DataNode> nodes)
    {
        var ordered = nodes
            .Select((node, index) => (node, index))
            .OrderBy(pair => pair, Comparer<(DataNode node, int index)>.Create((a, b) => {
                int cmp = Compare(a.node, b.node);
                return cmp != 0 ? cmp : a.index.CompareTo(b.index);
            }))
            .Select(pair => pair.node)
            .ToList();

        nodes.Clear();
        nodes.AddRange(ordered);
    }
}
