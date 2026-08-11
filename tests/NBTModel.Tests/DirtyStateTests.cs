using NBTExplorer.Model;
using Substrate.Core;
using Substrate.Nbt;
using Xunit;

namespace NBTModel.Tests;

/// <summary>
/// DataNode.CalculateChildModifiedState walks parents upward recomputing _childModified whenever
/// IsDataModified is set. The Avalonia NodeViewModel relies on that: the model's dirty state is
/// always correct, and the VM only re-raises PropertyChanged up its own chain. If these tests
/// break, RaiseModifiedChain is built on sand.
/// </summary>
public class DirtyStateTests
{
    [Fact]
    public void EditingALeafMarksEveryAncestorModified()
    {
        using var dir = new TempDir();
        var (file, leaf, mid) = BuildThreeLevelTree(dir);

        Assert.False(file.IsModified);
        Assert.False(mid.IsModified);

        using var _ = FormRegistryScope.EditScalarTo(new TagNodeInt(7));
        Assert.True(leaf.EditNode());

        Assert.True(leaf.IsModified);
        Assert.True(mid.IsModified);
        Assert.True(file.IsModified);
    }

    [Fact]
    public void SavingClearsDirtyStateThroughoutTheTree()
    {
        using var dir = new TempDir();
        var (file, leaf, mid) = BuildThreeLevelTree(dir);

        using var _ = FormRegistryScope.EditScalarTo(new TagNodeInt(7));
        leaf.EditNode();
        file.Save();

        Assert.False(leaf.IsModified);
        Assert.False(mid.IsModified);
        Assert.False(file.IsModified);
    }

    [Fact]
    public void CancelledEditLeavesTheTreeClean()
    {
        using var dir = new TempDir();
        var (file, leaf, _) = BuildThreeLevelTree(dir);

        using var _ = FormRegistryScope.CancelEverything();
        Assert.False(leaf.EditNode());
        Assert.False(file.IsModified);
    }

    [Fact]
    public void CollapseRefusesToReleaseAModifiedSubtree()
    {
        using var dir = new TempDir();
        var (file, leaf, mid) = BuildThreeLevelTree(dir);

        using var _ = FormRegistryScope.EditScalarTo(new TagNodeInt(7));
        leaf.EditNode();

        // The UI depends on this: collapsing a dirty node would silently discard the edit.
        mid.Collapse();
        Assert.True(mid.IsExpanded);
        Assert.NotEmpty(mid.Nodes);

        file.Save();
        mid.Collapse();
        Assert.False(mid.IsExpanded);
    }

    [Fact]
    public void DeletingATagMarksTheParentModified()
    {
        using var dir = new TempDir();
        var (file, leaf, mid) = BuildThreeLevelTree(dir);

        Assert.True(leaf.CanDeleteNode);
        Assert.True(leaf.DeleteNode());

        Assert.DoesNotContain(mid.Nodes, n => ReferenceEquals(n, leaf));
        Assert.True(file.IsModified);

        file.Save();

        var reloaded = NbtFileDataNode.TryCreateFrom(Path(dir))!;
        reloaded.Expand();
        var data = (TagCompoundDataNode)NbtFileRoundTripTests.Single(reloaded, "Data");
        data.Expand();
        Assert.DoesNotContain(data.Nodes, n => n.NodeName == "SpawnX");
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static string Path(TempDir dir) => dir.File("level.dat");

    /// <summary>file → Data (compound) → SpawnX (int leaf), all expanded.</summary>
    private static (NbtFileDataNode file, DataNode leaf, TagCompoundDataNode mid)
        BuildThreeLevelTree(TempDir dir)
    {
        string path = Path(dir);
        NbtFileRoundTripTests.WriteNbt(path, CompressionType.GZip,
                                       NbtFileRoundTripTests.SampleRoot(), "");

        var file = NbtFileDataNode.TryCreateFrom(path)!;
        file.Expand();
        var mid = (TagCompoundDataNode)NbtFileRoundTripTests.Single(file, "Data");
        mid.Expand();
        var leaf = NbtFileRoundTripTests.Single(mid, "SpawnX");
        return (file, leaf, mid);
    }
}
