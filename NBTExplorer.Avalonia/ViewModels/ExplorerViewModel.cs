using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NBTExplorer.Avalonia.Services;
using NBTExplorer.Model;

namespace NBTExplorer.Avalonia.ViewModels;

/// <summary>
/// The portable half of Controllers\NodeTreeController.cs, reshaped into an Explorer-style
/// two-pane model: a navigation tree of containers on the left, and the contents of one current
/// container in a details list on the right.
///
/// The TreeNode plumbing (FindFrontNode, CreateUnexpandedNode, UpdateNodeText,
/// BuildNodeContextMenu, the _context*_Click handlers — roughly 450 lines) is deliberately NOT
/// ported; bindings plus NodeViewModel.SyncChildren replace it.
/// </summary>
public sealed partial class ExplorerViewModel : ObservableObject
{
    private readonly ExplorerContext _ctx;
    private readonly RootDataNode _root = new();

    // Explorer-style history. Entries are ViewModels, so a node dropped by a refresh simply
    // stops being reachable rather than leaving a dangling path string.
    private readonly List<NodeViewModel> _back = [];
    private readonly List<NodeViewModel> _forward = [];

    // The nav pane both drives navigation (click a node) and reflects it (breadcrumb, Back, a
    // double-click in the details list). This guards the loop between the two.
    private bool _syncingNav;
    private NodeViewModel? _navSelected;

    public ExplorerViewModel(IIconMap icons)
    {
        _ctx = new ExplorerContext(icons);
        _ctx.SubtreeReleased += OnSubtreeReleased;
        RootNode = new NodeViewModel(_root, null, _ctx);
        Roots = RootNode.Children;
        NavRoots = RootNode.NavChildren;
        Items = [];
        Selection = [];
        Selection.CollectionChanged += (_, _) => {
            OnPropertyChanged(nameof(SelectionSummary));
            // The model raises no change notification, so command enablement is recomputed
            // explicitly — the equivalent of MainForm.UpdateUI on SelectionInvalidated.
            RaiseCommandStates();
        };
    }

    /// <summary>Invisible root; the nav tree binds to its children so several files can be open.</summary>
    public NodeViewModel RootNode { get; }

    /// <summary>Everything open — used by Save and Collapse all.</summary>
    public ObservableCollection<NodeViewModel> Roots { get; }

    /// <summary>What the navigation pane binds to: containers only.</summary>
    public ObservableCollection<NodeViewModel> NavRoots { get; }

    /// <summary>Contents of <see cref="CurrentFolder"/> — the details list on the right.</summary>
    public ObservableCollection<NodeViewModel> Items { get; }

    /// <summary>
    /// Pushed from MainWindow code-behind: Avalonia's SelectedItems is a plain CLR IList, not a
    /// StyledProperty, so it cannot be two-way bound in XAML.
    /// </summary>
    public ObservableCollection<NodeViewModel> Selection { get; }

    [ObservableProperty]
    public partial NodeViewModel? CurrentFolder { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    public bool HasOpenNodes => _root.Nodes.Count > 0;

    public bool IsAnyModified => _root.IsModified;

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => CurrentFolder?.Parent is { } p && !ReferenceEquals(p, RootNode);

    /// <summary>Address-bar segments: the chain from the first real node down to the current one.</summary>
    public IEnumerable<NodeViewModel> Breadcrumb =>
        CurrentFolder?.AncestorsAndSelf().Where(n => !ReferenceEquals(n, RootNode)) ?? [];

    public string SelectionSummary => Selection.Count switch {
        0 => $"{Items.Count} item(s)",
        1 => Selection[0].Display,
        var n => $"{n} of {Items.Count} selected",
    };

    // ---- navigation --------------------------------------------------------------------------

    /// <summary>
    /// Show a container's contents in the details list. Non-containers are ignored — in Explorer
    /// terms, you cannot navigate "into" a file.
    /// </summary>
    public void Navigate(NodeViewModel? node, bool recordHistory = true)
    {
        if (node is null || node.IsPlaceholder || !node.IsContainer)
            return;

        // Re-navigating to where we already are is a refresh, not a history entry — otherwise
        // the nav pane echoing the selection back would push a duplicate onto the Back stack.
        if (ReferenceEquals(CurrentFolder, node)) {
            node.EnsureExpanded();
            RefreshItems();
            return;
        }

        if (recordHistory && CurrentFolder is not null) {
            _back.Add(CurrentFolder);
            _forward.Clear();
        }

        node.EnsureExpanded();
        CurrentFolder = node;
    }

    /// <summary>
    /// A nav-pane collapse releases the whole subtree beneath the collapsed node. If we were
    /// standing inside it, the current folder now points at a DataNode with no parent — which
    /// renders as bare display text like "51 entries" instead of its tag name. Follow the
    /// collapse up to the node the user actually collapsed.
    /// </summary>
    private void OnSubtreeReleased(NodeViewModel collapsed)
    {
        _back.RemoveAll(n => n.IsDetached);
        _forward.RemoveAll(n => n.IsDetached);

        if (CurrentFolder?.IsDetached != true)
            return;

        Selection.Clear();
        Navigate(collapsed, recordHistory: false);
    }

    /// <summary>
    /// Keeps the navigation pane showing exactly the path to the current folder: the branch
    /// leading to it is expanded and selected, and every branch that is not on that path is
    /// collapsed.
    ///
    /// The collapse half matters as much as the expand half. Without it the tree accumulates
    /// every branch ever visited, so after a few minutes it is a wall of open nodes that no
    /// longer says anything about where you are — which is precisely what makes a tree feel like
    /// it has stopped following you.
    /// </summary>
    private void SyncNavigationPane()
    {
        if (_syncingNav)
            return;

        _syncingNav = true;
        try {
            var path = CurrentFolder is null
                ? []
                : new HashSet<NodeViewModel>(CurrentFolder.AncestorsAndSelf());

            foreach (var root in NavRoots.Where(n => !n.IsPlaceholder).ToList())
                ApplyPath(root, path);

            if (_navSelected is not null && !ReferenceEquals(_navSelected, CurrentFolder))
                _navSelected.IsSelected = false;

            _navSelected = CurrentFolder;
            if (_navSelected is not null && !ReferenceEquals(_navSelected, RootNode))
                _navSelected.IsSelected = true;
        }
        finally {
            _syncingNav = false;
        }
    }

    private static void ApplyPath(NodeViewModel node, HashSet<NodeViewModel> path)
    {
        if (!path.Contains(node)) {
            // Off the path. Collapsing releases the model subtree, which is also how memory gets
            // reclaimed after browsing a world's region files.
            node.IsExpanded = false;
            return;
        }

        node.IsExpanded = true;
        foreach (var child in node.NavChildren.Where(c => !c.IsPlaceholder).ToList())
            ApplyPath(child, path);
    }

    partial void OnCurrentFolderChanged(NodeViewModel? value)
    {
        SyncNavigationPane();
        RefreshItems();
        OnPropertyChanged(nameof(Breadcrumb));
        OnPropertyChanged(nameof(CanGoUp));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
        RaiseCommandStates();
    }

    /// <summary>Repopulates the details list from the current folder's children.</summary>
    public void RefreshItems()
    {
        Items.Clear();
        if (CurrentFolder is null)
            return;

        foreach (var child in CurrentFolder.Children.Where(c => !c.IsPlaceholder))
            Items.Add(child);

        OnPropertyChanged(nameof(SelectionSummary));
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (_back.Count == 0)
            return;

        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        if (CurrentFolder is not null)
            _forward.Add(CurrentFolder);

        Navigate(target, recordHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (_forward.Count == 0)
            return;

        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        if (CurrentFolder is not null)
            _back.Add(CurrentFolder);

        Navigate(target, recordHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp() => Navigate(CurrentFolder?.Parent);

    // ---- opening -----------------------------------------------------------------------------

    /// <summary>
    /// Mirrors NodeTreeController.OpenPaths: each path becomes a root child, dispatched through
    /// FileTypeRegistry / DirectoryDataNode exactly as the WinForms app does.
    /// </summary>
    public void OpenPaths(IEnumerable<string> paths)
    {
        var failures = new List<string>();
        int opened = 0;

        foreach (string path in paths) {
            DataNode? node = CreateNodeFor(path);
            if (node is null) {
                failures.Add(System.IO.Path.GetFileName(path));
                continue;
            }

            _root.Nodes.Add(node);
            opened++;
        }

        RootNode.SyncChildren();
        OnPropertyChanged(nameof(HasOpenNodes));

        if (opened > 0) {
            var newest = Roots[^1];
            newest.IsExpanded = true;
            Navigate(newest, recordHistory: false);
        }

        StatusText = failures.Count == 0
            ? $"Opened {opened} item(s)"
            : $"Opened {opened}, could not read: {string.Join(", ", failures)}";
    }

    private static DataNode? CreateNodeFor(string path)
    {
        if (Directory.Exists(path))
            return new DirectoryDataNode(path);

        if (!File.Exists(path))
            return null;

        // FileTypeRegistry maps name patterns to node types. It registers NbtFile/RegionFile/
        // CubicRegion in its OWN static constructor, so it primes itself on first access — no
        // startup-ordering dependency on MainForm's constructor the way the WinForms app had.
        foreach (var record in FileTypeRegistry.RegisteredTypes) {
            if (record.Value.NamePatternTest is null || !record.Value.NamePatternTest(path))
                continue;

            DataNode? node = record.Value.NodeCreate?.Invoke(path);
            if (node is not null)
                return node;
        }

        // Fall back to trying it as NBT regardless of its name — Open lets you pick any file.
        return NbtFileDataNode.TryCreateFrom(path);
    }

    [RelayCommand]
    private void CloseAll()
    {
        _root.Nodes.Clear();
        _ctx.Clear();
        _ctx.Register(RootNode);
        RootNode.SyncChildren();
        _back.Clear();
        _forward.Clear();
        CurrentFolder = null;
        Items.Clear();
        Selection.Clear();
        OnPropertyChanged(nameof(HasOpenNodes));
        OnPropertyChanged(nameof(IsAnyModified));
        StatusText = "Ready";
    }

    [RelayCommand]
    private void Refresh()
    {
        var node = CurrentFolder;
        if (node is null || node.IsPlaceholder)
            return;

        if (node.Model.CanRefreshNode)
            node.Model.RefreshNode();

        node.SyncChildren();
        node.RaiseSelf();
        RefreshItems();
        StatusText = $"Refreshed {node.Display}";
    }

    /// <summary>Reveal a node: navigate to its parent and select it in the details list.</summary>
    public void Reveal(NodeViewModel node)
    {
        if (node.Parent is { } parent)
            Navigate(parent);

        Selection.Clear();
        Selection.Add(node);
    }
}
