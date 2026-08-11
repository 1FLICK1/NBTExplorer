using CommunityToolkit.Mvvm.Input;
using NBTExplorer.Model;
using NBTModel.Interop;
using Substrate.Nbt;

namespace NBTExplorer.Avalonia.ViewModels;

/// <summary>
/// Editing half of the old NodeTreeController, ported as commands.
///
/// Each one keeps the original shape — <c>if (!node.CanX) return; if (node.X()) { refresh; }</c> —
/// because the interesting logic lives in NBTModel and the controller was only ever glue. What
/// the port adds is: re-reading the ViewModel projections afterwards (the model raises no change
/// notification, by design — see NodeViewModel's class comment) and keeping the details list in
/// step with the model's node collection.
/// </summary>
public sealed partial class ExplorerViewModel
{
    /// <summary>Nodes the commands act on: the details-list selection, or the current folder.</summary>
    private IReadOnlyList<NodeViewModel> Targets =>
        Selection.Count > 0 ? Selection.ToList()
        : CurrentFolder is not null ? [CurrentFolder]
        : [];

    private NodeViewModel? Target => Targets.Count == 1 ? Targets[0] : null;

    // ---- enablement --------------------------------------------------------------------------
    // Multi-select arbitration against GroupCapabilities is the next step; for now a command is
    // offered when every selected node individually supports it, which is the conservative subset.

    public bool CanEdit => Target is { IsPlaceholder: false } t && t.Model.CanEditNode;
    public bool CanRename => Target is { IsPlaceholder: false } t && t.Model.CanRenameNode;
    public bool CanDelete => Targets.Count > 0 && Targets.All(t => t.Model.CanDeleteNode);
    public bool CanCopy => Target is { IsPlaceholder: false } t && t.Model.CanCopyNode;
    public bool CanCut => Target is { IsPlaceholder: false } t && t.Model.CanCutNode;
    public bool CanPaste => CurrentFolder is { IsPlaceholder: false } f && f.Model.CanPasteIntoNode;
    public bool CanMoveUp => Target is { IsPlaceholder: false } t && t.Model.CanMoveNodeUp;
    public bool CanMoveDown => Target is { IsPlaceholder: false } t && t.Model.CanMoveNodeDown;
    public bool CanSave => IsAnyModified;

    /// <summary>Which tag types can be created inside the current folder, for the New menu.</summary>
    public IEnumerable<TagType> CreatableTypes
    {
        get {
            var folder = CurrentFolder;
            if (folder is null || folder.IsPlaceholder)
                yield break;

            foreach (TagType type in Enum.GetValues<TagType>()) {
                if (type != TagType.TAG_END && folder.Model.CanCreateTag(type))
                    yield return type;
            }
        }
    }

    /// <summary>
    /// Recomputes every command's enablement. The model raises nothing, so this is called after
    /// each operation and whenever the selection changes — the equivalent of MainForm.UpdateUI.
    /// </summary>
    public void RaiseCommandStates()
    {
        foreach (string name in new[] {
            nameof(CanEdit), nameof(CanRename), nameof(CanDelete), nameof(CanCopy),
            nameof(CanCut), nameof(CanPaste), nameof(CanMoveUp), nameof(CanMoveDown),
            nameof(CanSave), nameof(CreatableTypes), nameof(IsAnyModified),
        }) {
            OnPropertyChanged(name);
        }

        EditCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    // ---- commands ----------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanEdit))]
    public void Edit()
    {
        if (Target is not { } node || !node.Model.CanEditNode)
            return;

        if (node.Model.EditNode())
            AfterEdit(node, $"Edited {node.Name}");
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        if (Target is not { } node || !node.Model.CanRenameNode)
            return;

        if (node.Model.RenameNode())
            AfterEdit(node, $"Renamed to {node.Model.NodeName}");
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        var targets = Targets.Where(t => t.Model.CanDeleteNode).ToList();
        if (targets.Count == 0)
            return;

        var parent = targets[0].Parent;
        int deleted = targets.Count(t => t.Model.DeleteNode());

        Selection.Clear();
        AfterStructuralChange(parent, $"Deleted {deleted} item(s)");
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Copy()
    {
        if (Target is { } node && node.Model.CopyNode())
            StatusText = $"Copied {node.Name}";
        RaiseCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanCut))]
    private void Cut()
    {
        if (Target is not { } node || !node.Model.CanCutNode)
            return;

        var parent = node.Parent;
        if (node.Model.CutNode())
            AfterStructuralChange(parent, $"Cut {node.Name}");
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (CurrentFolder is not { } folder || !folder.Model.CanPasteIntoNode)
            return;

        if (folder.Model.PasteNode())
            AfterStructuralChange(folder, "Pasted");
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    private void Move(int offset)
    {
        if (Target is not { } node || !node.Model.CanReoderNode)
            return;

        if (!node.Model.ChangeRelativePosition(offset))
            return;

        var parent = node.Parent;
        parent?.SyncChildren();
        RefreshItems();

        // Keep the moved row selected so repeated presses keep moving the same node.
        Selection.Clear();
        Selection.Add(node);
        node.RaiseModifiedChain();
        RaiseCommandStates();
        StatusText = $"Moved {node.Name}";
    }

    /// <summary>Creates a tag of the given type inside the current folder.</summary>
    [RelayCommand]
    private void CreateTag(TagType type)
    {
        if (CurrentFolder is not { } folder || !folder.Model.CanCreateTag(type))
            return;

        if (folder.Model.CreateNode(type))
            AfterStructuralChange(folder, $"Added {NodeViewModel.FriendlyTagType(type)} tag");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var modified = Roots.Where(r => r.Model.IsModified).ToList();
        if (modified.Count == 0)
            return;

        var failed = new List<string>();
        foreach (var root in modified) {
            try {
                root.Model.Save();
            }
            catch (Exception ex) {
                // Saving is destructive and has no undo, so a failure must be reported, never
                // swallowed — a silent failure looks identical to success.
                failed.Add($"{root.Name}: {ex.Message}");
            }
            root.RaiseSelf();
        }

        foreach (var root in Roots)
            RaiseSubtreeModified(root);

        RefreshItems();
        RaiseCommandStates();
        OnPropertyChanged(nameof(IsAnyModified));

        StatusText = failed.Count == 0
            ? $"Saved {modified.Count} file(s)"
            : "Save failed — " + string.Join("; ", failed);

        if (failed.Count > 0)
            FormRegistry.MessageBox?.Invoke("Could not save:\n\n" + string.Join("\n", failed));
    }

    // ---- shared post-operation plumbing -------------------------------------------------------

    /// <summary>A value changed in place: the node's own projections and the dirty chain.</summary>
    private void AfterEdit(NodeViewModel node, string status)
    {
        node.RaiseSelf();
        node.RaiseModifiedChain();
        RefreshItems();
        RaiseCommandStates();
        OnPropertyChanged(nameof(IsAnyModified));
        StatusText = status;
    }

    /// <summary>Children were added or removed: the parent's collection has to be re-synced.</summary>
    private void AfterStructuralChange(NodeViewModel? parent, string status)
    {
        parent?.SyncChildren();
        parent?.RaiseSelf();
        parent?.RaiseModifiedChain();
        RefreshItems();
        RaiseCommandStates();
        OnPropertyChanged(nameof(IsAnyModified));
        StatusText = status;
    }

    private static void RaiseSubtreeModified(NodeViewModel node)
    {
        node.RaiseSelf();
        foreach (var child in node.Children.Where(c => !c.IsPlaceholder))
            RaiseSubtreeModified(child);
    }
}
