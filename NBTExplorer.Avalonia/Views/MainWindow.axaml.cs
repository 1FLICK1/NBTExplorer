using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NBTExplorer.Avalonia.Services;
using NBTExplorer.Avalonia.ViewModels;
using NBTExplorer.Avalonia.Views.Dialogs;

namespace NBTExplorer.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        OpenFileButton.Click += OnOpenFiles;
        OpenFolderButton.Click += OnOpenFolder;

        NavTree.SelectionChanged += OnNavSelectionChanged;
        // Tunnelling, so this runs before the TreeViewItem turns the press into a selection.
        NavTree.AddHandler(PointerPressedEvent, OnNavPointerPressed, RoutingStrategies.Tunnel);

        // SelectedItems is a plain CLR IList, not a StyledProperty, so it cannot be two-way
        // bound in XAML. Pushing it into the ViewModel from here is the standard fix.
        ItemsGrid.SelectionChanged += OnItemsSelectionChanged;
        ItemsGrid.DoubleTapped += OnItemsDoubleTapped;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        Opened += OnFirstOpened;
        Closing += OnClosing;
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private AvaloniaClipboardController? _clipboard;

    /// <summary>
    /// The model reaches the UI through two static seams — FormRegistry for dialogs and
    /// NbtClipboardController for copy/paste — and both need a window to own their dialogs, so
    /// they are registered once the window exists rather than at DI time.
    /// </summary>
    private void OnFirstOpened(object? sender, EventArgs e)
    {
        Opened -= OnFirstOpened;

        var icons = App.Services.GetRequiredService<IIconMap>();
        new AvaloniaFormHandlers(this, icons).Register();

        _clipboard = new AvaloniaClipboardController(this);
        _clipboard.Initialize();
        _clipboard.ClipboardChanged += () => Vm?.Explorer.RaiseCommandStates();

        Vm?.Explorer.RaiseCommandStates();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // A nested dispatcher frame is pumping a modal dialog; tearing the window down under it
        // would strand the frame and hang the process.
        if (UiThreadBlocking.IsModal) {
            e.Cancel = true;
            return;
        }

        if (Vm?.Explorer.IsAnyModified != true)
            return;

        var result = UiThreadBlocking.RunBlocking(() =>
            MessageDialog.Confirm("NBTExplorer", "You have unsaved changes.", "Save", "Discard")
                         .ShowDialog<MessageDialog.Result?>(this));

        switch (result) {
            case MessageDialog.Result.Primary:
                Vm.Explorer.SaveCommand.Execute(null);
                // If saving failed the tree is still dirty; do not close over the top of it.
                if (Vm.Explorer.IsAnyModified)
                    e.Cancel = true;
                break;
            case MessageDialog.Result.Secondary:
                break;   // discard and close
            default:
                e.Cancel = true;
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var explorer = Vm?.Explorer;
        if (explorer is null) {
            base.OnKeyDown(e);
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O) {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                _ = OpenFolderAsync();
            else
                _ = OpenFilesAsync();
            e.Handled = true;
            return;
        }

        // Explorer's navigation shortcuts.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) {
            switch (e.Key) {
                case Key.Left: explorer.GoBackCommand.Execute(null); e.Handled = true; return;
                case Key.Right: explorer.GoForwardCommand.Execute(null); e.Handled = true; return;
                case Key.Up: explorer.GoUpCommand.Execute(null); e.Handled = true; return;
            }
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
            switch (e.Key) {
                case Key.S: explorer.SaveCommand.Execute(null); e.Handled = true; return;
                case Key.X: explorer.CutCommand.Execute(null); e.Handled = true; return;
                case Key.C: explorer.CopyCommand.Execute(null); e.Handled = true; return;
                case Key.V: explorer.PasteCommand.Execute(null); e.Handled = true; return;
                case Key.Up: explorer.MoveUpCommand.Execute(null); e.Handled = true; return;
                case Key.Down: explorer.MoveDownCommand.Execute(null); e.Handled = true; return;
            }
        }

        switch (e.Key) {
            case Key.F5:
                explorer.RefreshCommand.Execute(null);
                e.Handled = true;
                return;
            case Key.F2:
                explorer.RenameCommand.Execute(null);
                e.Handled = true;
                return;
            case Key.Delete:
                explorer.DeleteCommand.Execute(null);
                e.Handled = true;
                return;
            case Key.Back:
                explorer.GoBackCommand.Execute(null);
                e.Handled = true;
                return;
            case Key.Enter when ItemsGrid.IsKeyboardFocusWithin:
                ActivateSelected();
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    // ---- opening -------------------------------------------------------------------------

    private void OnOpenFiles(object? sender, RoutedEventArgs e) => _ = OpenFilesAsync();

    private void OnOpenFolder(object? sender, RoutedEventArgs e) => _ = OpenFolderAsync();

    private async Task OpenFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Open NBT or region file",
            AllowMultiple = true,
            FileTypeFilter = [
                new FilePickerFileType("All supported") {
                    // Mirrors the formats README documents: standard NBT, schematic,
                    // uncompressed NBT, region (.mcr) and anvil (.mca).
                    Patterns = ["*.dat", "*.dat_old", "*.dat_mcr", "*.nbt", "*.schematic",
                                "*.bpt", "*.rc", "*.mcr", "*.mca"],
                },
                new FilePickerFileType("NBT files") {
                    Patterns = ["*.dat", "*.dat_old", "*.dat_mcr", "*.nbt", "*.schematic", "*.bpt", "*.rc"],
                },
                new FilePickerFileType("Region files") { Patterns = ["*.mcr", "*.mca"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        OpenPaths(files.Select(f => f.Path.LocalPath));
    }

    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            Title = "Open world or saves folder",
            AllowMultiple = true,
        });

        OpenPaths(folders.Select(f => f.Path.LocalPath));
    }

    private void OpenPaths(IEnumerable<string> paths)
    {
        var list = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (list.Count > 0)
            Vm?.Explorer.OpenPaths(list);
    }

    // ---- navigation ----------------------------------------------------------------------

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NavTree.SelectedItem is NodeViewModel node)
            Vm?.Explorer.Navigate(node);
    }

    /// <summary>
    /// How close to the chevron a click still counts as "expand this" rather than "select this".
    /// The stock glyph is a 6x12 arrow, which is a very small thing to hit; missing it selects
    /// the row and navigates instead, so the control reads as having ignored the click.
    /// </summary>
    private static readonly Thickness ChevronSlack = new(10, 6);

    private void OnNavPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;

        var item = source.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (item?.DataContext is not NodeViewModel node)
            return;

        // Only this item's own chevron — descendants have their own.
        var chevron = item.GetVisualDescendants()
            .OfType<ToggleButton>()
            .FirstOrDefault(t => t.Name == "PART_ExpandCollapseChevron"
                                 && ReferenceEquals(t.FindAncestorOfType<TreeViewItem>(), item));

        if (chevron is null || !chevron.IsVisible || !chevron.IsEffectivelyVisible)
            return;

        if (chevron.TransformToVisual(NavTree) is not { } transform)
            return;

        var target = new Rect(chevron.Bounds.Size).TransformToAABB(transform).Inflate(ChevronSlack);
        if (!target.Contains(e.GetPosition(NavTree)))
            return;

        node.IsExpanded = !node.IsExpanded;

        // Handled, so the press neither reaches the toggle button (which would undo this) nor
        // becomes a selection.
        e.Handled = true;
    }

    private void OnItemsDoubleTapped(object? sender, TappedEventArgs e) => ActivateSelected();

    /// <summary>
    /// Double-click / Enter: drill into containers the way double-clicking a folder does in
    /// Explorer, and open the value editor on a leaf.
    /// </summary>
    private void ActivateSelected()
    {
        if (ItemsGrid.SelectedItem is not NodeViewModel node || node.IsPlaceholder)
            return;

        if (node.IsContainer) {
            // The nav pane reveals, expands and highlights the new folder itself — see
            // ExplorerViewModel.SyncNavigationPane.
            Vm?.Explorer.Navigate(node);
            return;
        }

        Vm?.Explorer.EditCommand.Execute(null);
    }

    private void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NodeViewModel node })
            Vm?.Explorer.Navigate(node);
    }

    private void OnItemsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is null)
            return;

        var selection = Vm.Explorer.Selection;
        selection.Clear();
        foreach (var item in ItemsGrid.SelectedItems) {
            if (item is NodeViewModel { IsPlaceholder: false } node)
                selection.Add(node);
        }
    }

    // ---- drag and drop ---------------------------------------------------------------------

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.Data.GetFiles();
        if (items is null)
            return;

        OpenPaths(items.Select(i => i.Path.LocalPath));
    }
}
