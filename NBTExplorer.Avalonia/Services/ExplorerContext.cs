using NBTExplorer.Avalonia.ViewModels;
using NBTExplorer.Model;

namespace NBTExplorer.Avalonia.Services;

/// <summary>
/// Shared services and the DataNode → NodeViewModel identity map, handed to every NodeViewModel.
///
/// The identity map replaces NodeTreeController.FindFrontNode (which walked the tree looking for
/// the TreeNode whose .Tag was a given DataNode). "Reveal this search result" still needs that
/// resolution, but as an O(1) dictionary lookup rather than an O(depth x breadth) walk.
/// </summary>
public sealed class ExplorerContext
{
    private readonly Dictionary<DataNode, NodeViewModel> _map = new(ReferenceEqualityComparer.Instance);

    public ExplorerContext(IIconMap icons)
    {
        Icons = icons;
        Sorter = new NodeSorter();
    }

    public IIconMap Icons { get; }
    public NodeSorter Sorter { get; }

    public void Register(NodeViewModel vm) => _map[vm.Model] = vm;

    public void Unregister(DataNode node) => _map.Remove(node);

    public NodeViewModel? Find(DataNode node) => _map.GetValueOrDefault(node);

    public void Clear() => _map.Clear();

    /// <summary>
    /// Raised after a node has collapsed and released its descendants. Anything holding a
    /// reference into that subtree (the current folder, the navigation history) must drop it —
    /// a released DataNode has no parent and therefore no name.
    /// </summary>
    public event Action<NodeViewModel>? SubtreeReleased;

    internal void RaiseSubtreeReleased(NodeViewModel node) => SubtreeReleased?.Invoke(node);

    /// <summary>
    /// DataNode has no value equality, and two distinct nodes can compare equal by any
    /// field-based scheme, so identity is the only correct key.
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<DataNode>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(DataNode? x, DataNode? y) => ReferenceEquals(x, y);

        public int GetHashCode(DataNode obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
