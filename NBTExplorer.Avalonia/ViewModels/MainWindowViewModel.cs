using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NBTExplorer.Avalonia.ViewModels;

/// <summary>
/// Shell-level state: window title and the commands the command bar binds to.
/// The details pane is gone — in the Explorer layout the contents list carries that information
/// in its Type and Value columns, and the status bar carries the rest.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(ExplorerViewModel explorer)
    {
        Explorer = explorer;
        Explorer.PropertyChanged += (_, e) => {
            if (e.PropertyName is nameof(ExplorerViewModel.CurrentFolder)
                               or nameof(ExplorerViewModel.IsAnyModified))
                OnPropertyChanged(nameof(Title));
        };
    }

    public ExplorerViewModel Explorer { get; }

    public string Title
    {
        get {
            string modified = Explorer.IsAnyModified ? " •" : "";
            var current = Explorer.CurrentFolder;
            return current is null
                ? $"NBTExplorer{modified}"
                : $"{current.Name} — NBTExplorer{modified}";
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var root in Explorer.Roots)
            CollapseRecursive(root);
        Explorer.StatusText = "Collapsed all";
    }

    private static void CollapseRecursive(NodeViewModel node)
    {
        foreach (var child in node.Children.Where(c => !c.IsPlaceholder))
            CollapseRecursive(child);
        node.IsExpanded = false;
    }
}
