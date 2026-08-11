using NBTExplorer.Model;
using Substrate.Nbt;
using Xunit;

namespace NBTModel.Tests;

/// <summary>
/// Pins the NodeCapabilities contract that the command bar reads to decide what is enabled.
/// The WinForms NodeTreeController's capability arbitration is being ported to Avalonia; these
/// tests fix the per-node-type inputs that arbitration depends on.
/// </summary>
public class CapabilityTests
{
    [Fact]
    public void ScalarInACompoundCanBeEditedRenamedDeletedCopiedCut()
    {
        var (_, child) = CompoundWith("Value", new TagNodeInt(1));

        Assert.True(child.CanEditNode);
        Assert.True(child.CanRenameNode);     // parent is a named container
        Assert.True(child.CanDeleteNode);
        Assert.True(child.CanCopyNode);
        Assert.True(child.CanCutNode);
        Assert.False(child.CanReoderNode);    // compounds are unordered
        Assert.False(child.CanMoveNodeUp);
        Assert.False(child.CanMoveNodeDown);
    }

    [Fact]
    public void ScalarInAListCanBeReorderedButNotRenamed()
    {
        var list = new TagNodeList(TagType.TAG_INT);
        list.Add(new TagNodeInt(10));
        list.Add(new TagNodeInt(20));
        list.Add(new TagNodeInt(30));

        var node = new TagListDataNode(list);
        node.Expand();
        var children = node.Nodes.ToList();
        Assert.Equal(3, children.Count);

        // Lists are ordered but unnamed — this asymmetry is exactly what the multi-select
        // arbitration has to respect, and the most likely thing to break in the port.
        Assert.False(children[0].CanRenameNode);
        Assert.True(children[0].CanReoderNode);

        Assert.False(children[0].CanMoveNodeUp);
        Assert.True(children[0].CanMoveNodeDown);
        Assert.True(children[2].CanMoveNodeUp);
        Assert.False(children[2].CanMoveNodeDown);
    }

    [Fact]
    public void ReorderingAListChildMovesItInBothTheTagAndTheNodeTree()
    {
        var list = new TagNodeList(TagType.TAG_STRING);
        list.Add(new TagNodeString("a"));
        list.Add(new TagNodeString("b"));
        list.Add(new TagNodeString("c"));

        var node = new TagListDataNode(list);
        node.Expand();
        // DataNodeCollection implements its indexer EXPLICITLY on IList<DataNode>, so Nodes[i]
        // does not compile without a cast. Worth remembering when porting the tree ViewModels.
        var second = node.Nodes.ElementAt(1);

        Assert.True(second.ChangeRelativePosition(-1));

        Assert.Equal("b", list[0].ToTagString().Data);
        Assert.Equal("a", list[1].ToTagString().Data);
        Assert.Same(second, node.Nodes.First());
    }

    [Theory]
    [InlineData(TagType.TAG_BYTE)]
    [InlineData(TagType.TAG_SHORT)]
    [InlineData(TagType.TAG_INT)]
    [InlineData(TagType.TAG_LONG)]
    [InlineData(TagType.TAG_FLOAT)]
    [InlineData(TagType.TAG_DOUBLE)]
    [InlineData(TagType.TAG_STRING)]
    [InlineData(TagType.TAG_BYTE_ARRAY)]
    [InlineData(TagType.TAG_INT_ARRAY)]
    [InlineData(TagType.TAG_SHORT_ARRAY)]
    [InlineData(TagType.TAG_LONG_ARRAY)]
    [InlineData(TagType.TAG_LIST)]
    [InlineData(TagType.TAG_COMPOUND)]
    public void EveryTagTypeMapsToADataNode(TagType type)
    {
        var tag = TagDataNode.DefaultTag(type);
        Assert.NotNull(tag);

        var node = TagDataNode.CreateFromTag(tag);
        Assert.NotNull(node);
        Assert.Equal(type, tag.GetTagType());
    }

    [Fact]
    public void ContainersAreContainerTypesAndScalarsAreNot()
    {
        Assert.True(new TagCompoundDataNode(new TagNodeCompound()).IsContainerType);
        Assert.True(new TagListDataNode(new TagNodeList(TagType.TAG_INT)).IsContainerType);
        Assert.False(TagDataNode.CreateFromTag(new TagNodeInt(0))!.IsContainerType);
        Assert.False(TagDataNode.CreateFromTag(new TagNodeString(""))!.IsContainerType);
    }

    [Fact]
    public void ArrayTagsAreEditableInThisBuild()
    {
        // Guarded by the WINDOWS constant, which historically meant "this front-end ships a hex
        // editor". NBTModel.Net10.csproj defines it because the Avalonia app supplies
        // FormRegistry.EditByteArray.
        Assert.True(TagDataNode.CreateFromTag(new TagNodeByteArray(new byte[4]))!.CanEditNode);
        Assert.True(TagDataNode.CreateFromTag(new TagNodeIntArray(new int[4]))!.CanEditNode);
        Assert.True(TagDataNode.CreateFromTag(new TagNodeLongArray(new long[4]))!.CanEditNode);
    }

    [Fact]
    public void CompoundReportsUnexpandedChildrenOnlyWhenItHasSome()
    {
        var empty = new TagCompoundDataNode(new TagNodeCompound());
        Assert.False(empty.HasUnexpandedChildren);

        var full = new TagCompoundDataNode(new TagNodeCompound { ["x"] = new TagNodeInt(1) });
        Assert.True(full.HasUnexpandedChildren);
        full.Expand();
        Assert.False(full.HasUnexpandedChildren);
    }

    [Fact]
    public void RenamingAScalarUpdatesItsDisplayAndDirtyState()
    {
        var (parent, child) = CompoundWith("OldName", new TagNodeInt(5));

        using var _ = FormRegistryScope.RenameTo("NewName");
        Assert.True(child.RenameNode());

        Assert.Equal("NewName", child.NodeName);
        Assert.Equal("NewName: 5", child.NodeDisplay);
        Assert.True(parent.IsModified);
    }

    private static (TagCompoundDataNode parent, DataNode child) CompoundWith(string name, TagNode tag)
    {
        var parent = new TagCompoundDataNode(new TagNodeCompound { [name] = tag });
        parent.Expand();
        return (parent, parent.Nodes.Single());
    }
}
