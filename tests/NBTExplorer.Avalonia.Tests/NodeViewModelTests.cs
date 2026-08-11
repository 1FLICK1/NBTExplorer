using NBTExplorer.Avalonia.Services;
using NBTExplorer.Avalonia.ViewModels;
using NBTExplorer.Model;
using Substrate.Nbt;
using Xunit;

namespace NBTExplorer.Avalonia.Tests;

public class NodeViewModelTests
{
    private static (NodeViewModel vm, ExplorerContext ctx) Wrap(DataNode model)
    {
        var ctx = new ExplorerContext(new IconMap());
        return (new NodeViewModel(model, null, ctx), ctx);
    }

    private static TagCompoundDataNode SampleCompound() => new(new TagNodeCompound {
        ["SpawnX"] = new TagNodeInt(128),
        ["LevelName"] = new TagNodeString("Probe"),
        ["Player"] = new TagNodeCompound { ["Health"] = new TagNodeFloat(20f) },
    });

    // ---- lazy expansion ----------------------------------------------------------------------

    [Fact]
    public void UnexpandedContainerShowsOnlyThePlaceholder()
    {
        var (vm, _) = Wrap(SampleCompound());

        // The chevron must appear without reading anything. For a region file, materialising the
        // real children here would mean parsing up to 1024 chunks off disk just to draw a
        // triangle — which is why the sentinel exists.
        Assert.Single(vm.Children);
        Assert.True(vm.Children[0].IsPlaceholder);
        Assert.False(vm.Model.IsExpanded);
    }

    [Fact]
    public void LeafHasNoChildrenAndThereforeNoChevron()
    {
        var (vm, _) = Wrap(TagDataNode.CreateFromTag(new TagNodeInt(1))!);
        Assert.Empty(vm.Children);
    }

    [Fact]
    public void ExpandingReplacesThePlaceholderWithRealChildren()
    {
        var (vm, _) = Wrap(SampleCompound());

        vm.IsExpanded = true;

        Assert.True(vm.Model.IsExpanded);
        Assert.Equal(3, vm.Children.Count);
        Assert.DoesNotContain(vm.Children, c => c.IsPlaceholder);
    }

    [Fact]
    public void ExpandedChildrenAreSortedContainersFirstThenNaturally()
    {
        var (vm, _) = Wrap(SampleCompound());
        vm.IsExpanded = true;

        // NodeTreeComparer ordering: compounds (0), lists (1), scalars (2).
        Assert.Equal("Player", vm.Children[0].Model.NodeName);
        Assert.Equal(["LevelName", "SpawnX"],
                     vm.Children.Skip(1).Select(c => c.Model.NodeName));
    }

    [Fact]
    public void ListChildrenKeepInsertionOrder()
    {
        var list = new TagNodeList(TagType.TAG_STRING);
        list.Add(new TagNodeString("zulu"));
        list.Add(new TagNodeString("alpha"));
        list.Add(new TagNodeString("mike"));

        var (vm, _) = Wrap(new TagListDataNode(list));
        vm.IsExpanded = true;

        // TAG_LIST index IS the data. Sorting these alphabetically would silently reorder the
        // user's list — NodeSorter returns 0 for list children specifically to prevent that.
        Assert.Equal(["zulu", "alpha", "mike"],
                     vm.Children.Select(c => c.Model.NodeDisplay));
    }

    // ---- collapse ----------------------------------------------------------------------------

    [Fact]
    public void CollapsingACleanNodeReleasesItAndRestoresThePlaceholder()
    {
        var (vm, _) = Wrap(SampleCompound());
        vm.IsExpanded = true;
        Assert.Equal(3, vm.Children.Count);

        vm.IsExpanded = false;

        Assert.False(vm.Model.IsExpanded);
        Assert.Single(vm.Children);
        Assert.True(vm.Children[0].IsPlaceholder);
    }

    [Fact]
    public void CollapsingADirtyNodeKeepsItsChildren()
    {
        var compound = SampleCompound();
        var (vm, _) = Wrap(compound);
        vm.IsExpanded = true;

        // Mark the subtree dirty the way an edit would.
        var child = vm.Children.Single(c => c.Model.NodeName == "SpawnX");
        MarkDirty(child.Model);

        vm.IsExpanded = false;

        // DataNode.Collapse refuses to release a modified subtree; discarding the ViewModels here
        // would drop the user's unsaved edit from the UI while the model still held it.
        Assert.Equal(3, vm.Children.Count);
        Assert.True(vm.Model.IsExpanded);
    }

    // ---- identity-map reconcile --------------------------------------------------------------

    [Fact]
    public void SyncChildrenReusesViewModelsForSurvivingNodes()
    {
        var (vm, _) = Wrap(SampleCompound());
        vm.IsExpanded = true;

        var before = vm.Children.ToDictionary(c => c.Model);
        var player = vm.Children.Single(c => c.Model.NodeName == "Player");
        player.IsExpanded = true;

        vm.SyncChildren();

        // Reusing the instance is what preserves expansion and selection across a refresh.
        foreach (var child in vm.Children)
            Assert.Same(before[child.Model], child);
        Assert.True(player.IsExpanded);
    }

    [Fact]
    public void SyncChildrenPicksUpAddedAndRemovedNodes()
    {
        var tag = new TagNodeCompound { ["A"] = new TagNodeInt(1), ["B"] = new TagNodeInt(2) };
        var (vm, _) = Wrap(new TagCompoundDataNode(tag));
        vm.IsExpanded = true;
        Assert.Equal(2, vm.Children.Count);

        // Simulate a delete followed by a create, at the DataNode level.
        var removed = vm.Model.Nodes.Single(n => n.NodeName == "B");
        vm.Model.Nodes.Remove(removed);
        tag.Remove("B");
        tag["C"] = new TagNodeInt(3);
        vm.Model.Nodes.Add(TagDataNode.CreateFromTag(tag["C"])!);

        vm.SyncChildren();

        Assert.Equal(["A", "C"], vm.Children.Select(c => c.Model.NodeName));
    }

    [Fact]
    public void SyncChildrenNeverReparentsDataNodes()
    {
        // DataNodeCollection.Add/Insert throw when the node already has a parent. SyncChildren
        // rebuilds ViewModels only; if it ever touched the model collection this would blow up.
        var (vm, _) = Wrap(SampleCompound());
        vm.IsExpanded = true;

        var exception = Record.Exception(() => {
            vm.SyncChildren();
            vm.SyncChildren();
            vm.SyncChildren();
        });

        Assert.Null(exception);
        Assert.Equal(3, vm.Children.Count);
    }

    [Fact]
    public void CollapsingPrunesTheIdentityMapSoDeadSubtreesAreNotPinned()
    {
        var compound = SampleCompound();
        var (vm, ctx) = Wrap(compound);
        vm.IsExpanded = true;

        var player = vm.Children.Single(c => c.Model.NodeName == "Player");
        player.IsExpanded = true;
        var health = player.Children.Single();
        Assert.Same(health, ctx.Find(health.Model));

        vm.IsExpanded = false;

        Assert.Null(ctx.Find(health.Model));
        Assert.Null(ctx.Find(player.Model));
    }

    // ---- navigation --------------------------------------------------------------------------

    [Fact]
    public void BreadcrumbRunsRootToLeaf()
    {
        var (vm, _) = Wrap(SampleCompound());
        vm.IsExpanded = true;
        var player = vm.Children.Single(c => c.Model.NodeName == "Player");
        player.IsExpanded = true;
        var health = player.Children.Single();

        Assert.Equal([vm, player, health], health.AncestorsAndSelf());
    }

    [Fact]
    public void ExpandAncestorsRevealsADeepNodeWithoutExpandingItself()
    {
        var (vm, _) = Wrap(SampleCompound());
        vm.IsExpanded = true;
        var player = vm.Children.Single(c => c.Model.NodeName == "Player");
        player.IsExpanded = true;
        var health = player.Children.Single();

        player.IsExpanded = false;
        vm.IsExpanded = false;

        health.ExpandAncestors();

        Assert.True(vm.IsExpanded);
        Assert.True(player.IsExpanded);
        Assert.False(health.IsExpanded);
    }

    [Fact]
    public void RaiseModifiedChainNotifiesEveryAncestor()
    {
        var (vm, _) = Wrap(SampleCompound());
        vm.IsExpanded = true;
        var player = vm.Children.Single(c => c.Model.NodeName == "Player");
        player.IsExpanded = true;
        var health = player.Children.Single();

        var notified = new List<NodeViewModel>();
        foreach (var node in new[] { vm, player, health }) {
            var captured = node;
            captured.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(NodeViewModel.IsModified))
                    notified.Add(captured);
            };
        }

        health.RaiseModifiedChain();

        Assert.Equal([health, player, vm], notified);
    }

    /// <summary>
    /// IsDataModified is protected, so reach it the way the app does: through an edit. This
    /// stubs the scalar editor rather than reflecting into the model.
    /// </summary>
    private static void MarkDirty(DataNode node)
    {
        var previous = NBTModel.Interop.FormRegistry.EditTagScalar;
        try {
            NBTModel.Interop.FormRegistry.EditTagScalar = _ => true;
            Assert.True(node.EditNode());
        }
        finally {
            NBTModel.Interop.FormRegistry.EditTagScalar = previous;
        }
    }
}
