using NBTExplorer.Avalonia.Services;
using NBTExplorer.Avalonia.ViewModels;
using Substrate.Core;
using Substrate.Nbt;
using Xunit;

namespace NBTExplorer.Avalonia.Tests;

/// <summary>
/// The navigation pane has to show where you are, not where you have been. It must follow the
/// current folder no matter what moved it — the details list, the breadcrumb, Back — and it must
/// collapse branches that are no longer on the path, or it accumulates every node ever opened
/// and stops meaning anything.
/// </summary>
public class NavigationPaneTests : IDisposable
{
    private readonly string _dir;
    private readonly ExplorerViewModel _vm;

    public NavigationPaneTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "NBTNav_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "level.dat");

        // Two sibling branches, each two levels deep, so "off the path" is meaningful.
        var root = new TagNodeCompound {
            ["Data"] = new TagNodeCompound {
                ["Alpha"] = new TagNodeCompound {
                    ["AlphaInner"] = new TagNodeCompound { ["x"] = new TagNodeInt(1) },
                },
                ["Beta"] = new TagNodeCompound {
                    ["BetaInner"] = new TagNodeCompound { ["y"] = new TagNodeInt(2) },
                },
                ["Scalar"] = new TagNodeInt(7),
            },
        };
        using (Stream s = new NBTFile(path).GetDataOutputStream(CompressionType.GZip))
            new NbtTree(root, "").WriteTo(s);

        _vm = new ExplorerViewModel(new IconMap());
        _vm.OpenPaths([path]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private NodeViewModel Go(params string[] names)
    {
        var node = _vm.Roots.Single();
        _vm.Navigate(node);
        foreach (string name in names) {
            node = _vm.Items.Single(i => i.Name == name);
            _vm.Navigate(node);
        }
        return node;
    }

    private NodeViewModel Nav(params string[] names)
    {
        var node = _vm.NavRoots.Single(n => !n.IsPlaceholder);
        foreach (string name in names)
            node = node.NavChildren.Single(c => c.Name == name);
        return node;
    }

    [Fact]
    public void NavigatingFromTheDetailsListSelectsTheFolderInTheNavPane()
    {
        var alpha = Go("Data", "Alpha");

        Assert.True(alpha.IsSelected);
        Assert.Same(alpha, _vm.CurrentFolder);
    }

    [Fact]
    public void TheWholePathToTheCurrentFolderIsExpanded()
    {
        Go("Data", "Alpha", "AlphaInner");

        Assert.True(Nav().IsExpanded);                     // level.dat
        Assert.True(Nav("Data").IsExpanded);
        Assert.True(Nav("Data", "Alpha").IsExpanded);
    }

    [Fact]
    public void OnlyOneNodeIsSelectedAtATime()
    {
        var alpha = Go("Data", "Alpha");
        var beta = Go("Data", "Beta");

        Assert.False(alpha.IsSelected);
        Assert.True(beta.IsSelected);
    }

    [Fact]
    public void BranchesOffThePathAreCollapsed()
    {
        Go("Data", "Alpha", "AlphaInner");
        Assert.True(Nav("Data", "Alpha").IsExpanded);

        // Walk over to the sibling branch.
        Go("Data", "Beta", "BetaInner");

        Assert.True(Nav("Data", "Beta").IsExpanded);
        Assert.False(Nav("Data", "Alpha").IsExpanded);
    }

    [Fact]
    public void ReturningToTheTopCollapsesEverythingBelowIt()
    {
        Go("Data", "Alpha", "AlphaInner");

        // The complaint: going back to the start still left the tree open several levels deep.
        var file = _vm.Roots.Single();
        _vm.Navigate(file);

        Assert.True(file.IsExpanded);
        Assert.False(Nav("Data").IsExpanded);
    }

    [Fact]
    public void GoingBackMovesTheNavSelectionToo()
    {
        Go("Data", "Alpha");
        var inner = Go("Data", "Alpha", "AlphaInner");
        Assert.True(inner.IsSelected);

        _vm.GoBackCommand.Execute(null);

        Assert.False(inner.IsSelected);
        Assert.True(Nav("Data", "Alpha").IsSelected);
    }

    [Fact]
    public void GoingUpMovesTheNavSelectionToo()
    {
        Go("Data", "Alpha", "AlphaInner");

        _vm.GoUpCommand.Execute(null);

        Assert.Equal("Alpha", _vm.CurrentFolder!.Name);
        Assert.True(Nav("Data", "Alpha").IsSelected);
    }

    [Fact]
    public void SelectingInTheNavPaneDoesNotPushDuplicateHistoryEntries()
    {
        Go("Data", "Alpha");

        // The pane echoes the selection back through SelectionChanged; navigating to where we
        // already are must be a no-op rather than another Back entry.
        _vm.Navigate(_vm.CurrentFolder);
        _vm.Navigate(_vm.CurrentFolder);

        _vm.GoBackCommand.Execute(null);
        Assert.Equal("Data", _vm.CurrentFolder!.Name);
    }

    [Fact]
    public void NavPaneNeverListsNonContainers()
    {
        Go("Data");

        // "Scalar" is an Int and belongs in the details list only.
        Assert.Contains(_vm.Items, i => i.Name == "Scalar");
        Assert.DoesNotContain(Nav("Data").NavChildren, c => c.Name == "Scalar");
    }
}
