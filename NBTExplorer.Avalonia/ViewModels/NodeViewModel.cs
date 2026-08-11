using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NBTExplorer.Avalonia.Services;
using NBTExplorer.Model;
using Substrate.Nbt;

namespace NBTExplorer.Avalonia.ViewModels;

/// <summary>
/// Binding wrapper around a <see cref="DataNode"/>.
///
/// Why a wrapper instead of putting INotifyPropertyChanged on DataNode itself: the model has no
/// choke point where change notification could be raised. TagDataNode.EditScalarValue hands the
/// dialog the raw Substrate TagNode and the dialog mutates it IN PLACE — the DataNode is never
/// told, and NodeDisplay is computed from that foreign object. INPC would therefore stay silent
/// during the single most common user action. RenameNode and ChangeRelativePosition have the
/// same shape.
///
/// So this class pulls: display properties have no backing fields, and callers re-raise
/// explicitly after an operation. That is the same contract NodeTreeController.RefreshChildNodes
/// has used for over a decade.
/// </summary>
public sealed partial class NodeViewModel : ObservableObject
{
    /// <summary>
    /// Stand-in child that makes the expander chevron appear without touching the file.
    /// Mirrors NodeTreeController.CreateUnexpandedNode's empty TreeNode: Avalonia only draws the
    /// chevron when ItemsSource is non-empty, and materialising the real children here would
    /// mean reading up to 1024 chunks off disk per region file just to draw a triangle.
    /// </summary>
    public static readonly NodeViewModel Placeholder = new();

    private readonly ExplorerContext _ctx = null!;
    private ObservableCollection<NodeViewModel>? _children;
    private ObservableCollection<NodeViewModel>? _navChildren;

    private NodeViewModel()
    {
        Model = new DataNode();
        IsPlaceholder = true;
    }

    public NodeViewModel(DataNode model, NodeViewModel? parent, ExplorerContext ctx)
    {
        Model = model;
        Parent = parent;
        _ctx = ctx;
        ctx.Register(this);
    }

    public DataNode Model { get; }
    public NodeViewModel? Parent { get; }
    public bool IsPlaceholder { get; }

    /// <summary>
    /// True once the backing DataNode has been released from the tree. DataNode.Release clears
    /// the child collection, which sets every child's Parent to null — and a TagDataNode with no
    /// parent has no name, so a detached node renders as bare display text ("51 entries"). Anyone
    /// holding a reference across a collapse has to check this.
    /// </summary>
    public bool IsDetached { get; private set; }

    // ---- pull-based projections; refreshed by explicit Raise* calls ---------------------------

    // The placeholder never reaches the user, but Avalonia still realises a container for it and
    // evaluates the bindings, so every projection has to tolerate its null context.
    public string Display => IsPlaceholder ? "" : Model.NodeDisplay;
    public string Path => IsPlaceholder ? "" : Model.NodePath;
    public bool IsModified => !IsPlaceholder && Model.IsModified;
    public bool IsContainer => !IsPlaceholder && Model.IsContainerType;
    public string IconKey => IsPlaceholder ? "Icon.Unknown" : _ctx.Icons.IconKey(Model);
    public string BrushKey => IsPlaceholder ? "TagNeutralBrush" : _ctx.Icons.BrushKey(Model);

    // ---- details-view columns ----------------------------------------------------------------

    /// <summary>
    /// DataNode.NodeName is empty for anything that is not a named tag — directories, files and
    /// list elements all return "" or null — so fall back to the display text, which every node
    /// type implements.
    /// </summary>
    public string Name
    {
        get {
            if (IsPlaceholder)
                return "";
            string? name = Model.NodeName;
            if (!string.IsNullOrEmpty(name))
                return name;
            // List elements are addressed by index; NodePathName yields that.
            return Parent?.Model is TagListDataNode ? $"[{Model.NodePathName}]" : Model.NodeDisplay;
        }
    }

    public string TypeName => IsPlaceholder ? "" : DescribeType(Model);

    /// <summary>
    /// What goes in the Value column. Containers report their size rather than a value, matching
    /// how Explorer shows a folder's contents count instead of a file size.
    /// </summary>
    public string ValueText
    {
        get {
            if (IsPlaceholder)
                return "";
            return Model switch {
                // Arrays: TagNode*Array.ToString() returns "System.Int32[]", which tells the user
                // nothing. Report the element count instead, like the tree display does.
                TagDataNode { Tag: TagNodeByteArray a } => Count(a.Length, "byte"),
                TagDataNode { Tag: TagNodeIntArray a } => Count(a.Length, "int"),
                TagDataNode { Tag: TagNodeShortArray a } => Count(a.Length, "short"),
                TagDataNode { Tag: TagNodeLongArray a } => Count(a.Length, "long"),

                // Invariant formatting, not tag.ToString(): on a ru-RU machine the default
                // renders 0.2 as "0,2", which then fails to round-trip through the editor —
                // that parses invariant, because NBT is a binary interchange format and a file
                // must not read differently depending on who opened it.
                TagDataNode { IsContainerType: false } tag => Services.TagValues.Format(tag.Tag),
                TagDataNode.Container c => Plural(c.TagCount),
                NbtFileDataNode f => Plural(f.TagCount),
                _ when Model.IsContainerType && Model.IsExpanded => Plural(Model.Nodes.Count),
                _ => "",
            };

            static string Plural(int n) => n == 1 ? "1 entry" : $"{n} entries";
            static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
        }
    }

    /// <summary>Drives whether the Value cell accepts a click-to-edit.</summary>
    public bool IsValueEditable => !IsPlaceholder && Model.CanEditNode;

    private static string DescribeType(DataNode model) => model switch {
        // RootDataNode derives from TagCompoundDataNode, so it has to be matched first.
        RootDataNode => "Workspace",
        TagDataNode tag => FriendlyTagType(tag.Tag.GetTagType()),
        NbtFileDataNode => "NBT file",
        RegionFileDataNode => "Region file",
        RegionChunkDataNode => "Chunk",
        CubicRegionDataNode => "Cubic region",
        DirectoryDataNode => "Folder",
        _ => model.GetType().Name,
    };

    /// <summary>
    /// The raw enum names (TAG_BYTE_ARRAY) are shouty and too wide for a details column. These
    /// are the names the NBT specification uses in prose, which is what a user recognises.
    /// </summary>
    public static string FriendlyTagType(TagType type) => type switch {
        TagType.TAG_BYTE => "Byte",
        TagType.TAG_SHORT => "Short",
        TagType.TAG_INT => "Int",
        TagType.TAG_LONG => "Long",
        TagType.TAG_FLOAT => "Float",
        TagType.TAG_DOUBLE => "Double",
        TagType.TAG_STRING => "String",
        TagType.TAG_BYTE_ARRAY => "Byte array",
        TagType.TAG_INT_ARRAY => "Int array",
        TagType.TAG_SHORT_ARRAY => "Short array",
        TagType.TAG_LONG_ARRAY => "Long array",
        TagType.TAG_LIST => "List",
        TagType.TAG_COMPOUND => "Compound",
        TagType.TAG_END => "End",
        _ => type.ToString(),
    };

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Live filter (Phase 4). Bound to TreeViewItem.IsVisible.</summary>
    [ObservableProperty]
    public partial bool IsFilteredIn { get; set; } = true;


    /// <summary>Deep-search highlight (Phase 4).</summary>
    [ObservableProperty]
    public partial bool IsSearchHit { get; set; }

    /// <summary>Every child — what the details list shows.</summary>
    public ObservableCollection<NodeViewModel> Children => _children ??= BuildInitialChildren();

    /// <summary>
    /// Container children only — what the navigation pane shows, mirroring how Explorer's tree
    /// lists folders but not files.
    ///
    /// This has to be its own collection rather than Children with the non-containers hidden:
    /// a TreeViewItem draws its expander whenever ItemsSource is non-empty, so binding the full
    /// set gave a chevron to every compound — including ones holding nothing but scalars, where
    /// expanding visibly did nothing.
    /// </summary>
    public ObservableCollection<NodeViewModel> NavChildren => _navChildren ??= BuildInitialNavChildren();

    private ObservableCollection<NodeViewModel> BuildInitialChildren()
        => Model.HasUnexpandedChildren || Model.Nodes.Count > 0
            ? [Placeholder]
            : [];

    /// <summary>
    /// Whether the nav pane should offer an expander before this node has been expanded.
    ///
    /// For tag-backed nodes the whole NBT tree is already in memory once the parent expanded, so
    /// we can look and give an honest answer — a compound of 51 strings gets no chevron. For
    /// nodes backed by the file system (directories, region files) answering truthfully would
    /// mean reading up to 1024 chunks per file just to draw a triangle, so those stay optimistic
    /// and the chevron disappears on first expand.
    /// </summary>
    private ObservableCollection<NodeViewModel> BuildInitialNavChildren()
        => MightHaveContainerChildren() ? [Placeholder] : [];

    private bool MightHaveContainerChildren()
    {
        if (Model.Nodes.Count > 0)
            return Model.Nodes.Any(n => n.IsContainerType);

        return Model switch {
            TagCompoundDataNode c when c.Tag is TagNodeCompound tag
                => tag.Values.Any(IsContainerTag),
            TagListDataNode l when l.Tag is TagNodeList list
                => list.Count > 0 && IsContainerType(list.ValueType),
            _ => Model.HasUnexpandedChildren,
        };

        static bool IsContainerTag(TagNode tag) => IsContainerType(tag.GetTagType());
        static bool IsContainerType(TagType type)
            => type is TagType.TAG_COMPOUND or TagType.TAG_LIST;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (IsPlaceholder)
            return;

        if (value)
            EnsureExpanded();
        else
            CollapseIfClean();
    }

    /// <summary>Mirrors NodeTreeController.ExpandNode (line 547).</summary>
    public void EnsureExpanded()
    {
        if (!Model.IsExpanded) {
            Model.Expand();
            RaiseSelf();
        }
        SyncChildren();
    }

    /// <summary>
    /// Mirrors NodeTreeController.CollapseNode (line 574). DataNode.Collapse refuses to release
    /// a modified subtree — collapsing a dirty node would discard the user's edit — so this must
    /// not clear the children in that case either.
    /// </summary>
    private void CollapseIfClean()
    {
        if (Model.IsModified)
            return;

        Model.Collapse();

        if (_children is null)
            return;

        foreach (var child in _children.Where(c => !c.IsPlaceholder).ToList())
            child.Detach();

        _children.Clear();
        _navChildren?.Clear();
        if (Model.HasUnexpandedChildren)
            _children.Add(Placeholder);
        if (_navChildren is not null && MightHaveContainerChildren())
            _navChildren.Add(Placeholder);

        // Raised only once the collapse has fully finished, so a listener is free to navigate
        // without fighting the teardown it is reacting to.
        _ctx.RaiseSubtreeReleased(this);
    }

    /// <summary>
    /// Identity-map reconcile, ported from NodeTreeController.RefreshChildNodes (line 713):
    /// ViewModels whose backing DataNode survived are reused, so expansion and selection state
    /// is preserved across a refresh.
    ///
    /// IMPORTANT: this rebuilds ViewModels only. It must never add a DataNode to a collection —
    /// DataNodeCollection.Add/Insert throw when the node already has a parent.
    /// </summary>
    public void SyncChildren()
    {
        var children = Children;

        var surviving = children
            .Where(c => !c.IsPlaceholder)
            .ToDictionary(c => c.Model, c => c);

        var models = Model.Nodes.ToList();
        _ctx.Sorter.Sort(models);

        var rebuilt = new List<NodeViewModel>(models.Count);
        foreach (var model in models) {
            if (surviving.Remove(model, out var existing))
                rebuilt.Add(existing);
            else
                rebuilt.Add(new NodeViewModel(model, this, _ctx));
        }

        // Anything left in `surviving` was removed from the model — drop it from the identity map
        // so the dictionary does not pin dead subtrees in memory.
        foreach (var orphan in surviving.Values)
            orphan.Detach();

        ReplaceInPlace(children, rebuilt);
        ReplaceInPlace(NavChildren, rebuilt.Where(vm => vm.IsContainer).ToList());
    }

    /// <summary>
    /// Minimal diff rather than Clear()+AddRange, so Avalonia does not tear down and rebuild
    /// every container — which would lose scroll position and collapse the whole subtree.
    /// </summary>
    private static void ReplaceInPlace(ObservableCollection<NodeViewModel> target,
                                       List<NodeViewModel> desired)
    {
        for (int i = 0; i < desired.Count; i++) {
            if (i < target.Count) {
                if (!ReferenceEquals(target[i], desired[i]))
                    target[i] = desired[i];
            }
            else {
                target.Add(desired[i]);
            }
        }

        while (target.Count > desired.Count)
            target.RemoveAt(target.Count - 1);
    }

    private void Detach()
    {
        IsDetached = true;
        _ctx.Unregister(Model);
        if (_children is null)
            return;

        foreach (var child in _children.Where(c => !c.IsPlaceholder).ToList())
            child.Detach();
        _children.Clear();
        _navChildren?.Clear();
    }

    /// <summary>Re-reads every projection off the model. Call after any operation on this node.</summary>
    public void RaiseSelf()
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IconKey));
        OnPropertyChanged(nameof(BrushKey));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TypeName));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(IsValueEditable));
    }

    /// <summary>
    /// DataNode.CalculateChildModifiedState (DataNode.cs:113) already walks parents upward
    /// recomputing _childModified, so the MODEL state is always correct after an edit. All the
    /// ViewModel layer has to do is re-raise PropertyChanged up its own chain, in O(depth).
    /// </summary>
    public void RaiseModifiedChain()
    {
        for (NodeViewModel? n = this; n is not null; n = n.Parent)
            n.OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>Root-to-here chain, used by the breadcrumb bar.</summary>
    public IEnumerable<NodeViewModel> AncestorsAndSelf()
    {
        var chain = new List<NodeViewModel>();
        for (NodeViewModel? n = this; n is not null; n = n.Parent)
            chain.Add(n);
        chain.Reverse();
        return chain;
    }

    /// <summary>Expands every ancestor so this node becomes visible in the tree.</summary>
    public void ExpandAncestors()
    {
        foreach (var ancestor in AncestorsAndSelf().SkipLast(1))
            ancestor.IsExpanded = true;
    }
}
