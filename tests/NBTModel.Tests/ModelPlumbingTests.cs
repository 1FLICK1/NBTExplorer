using NBTExplorer.Model;
using NBTExplorer.Utility;
using NBTModel.Interop;
using Substrate.Nbt;
using Xunit;

namespace NBTModel.Tests;

public class DataNodeCollectionTests
{
    /// <summary>
    /// Insert/Add throw when the node already has a parent. The Avalonia SyncChildren rebuilds
    /// ViewModels only and must never re-add a DataNode to a collection — this is the guard that
    /// will catch it if that rule is violated.
    /// </summary>
    [Fact]
    public void AddingAnAlreadyParentedNodeThrows()
    {
        var a = new RootDataNode();
        var b = new RootDataNode();
        var child = new TagCompoundDataNode(new TagNodeCompound());

        a.Nodes.Add(child);
        Assert.Same(a, child.Parent);

        Assert.Throws<ArgumentException>(() => b.Nodes.Add(child));
        Assert.Throws<ArgumentException>(() => b.Nodes.Insert(0, child));
    }

    [Fact]
    public void RemovingClearsTheParentSoTheNodeCanBeReattached()
    {
        var a = new RootDataNode();
        var b = new RootDataNode();
        var child = new TagCompoundDataNode(new TagNodeCompound());

        a.Nodes.Add(child);
        Assert.True(a.Nodes.Remove(child));
        Assert.Null(child.Parent);

        b.Nodes.Add(child);
        Assert.Same(b, child.Parent);
    }

    [Fact]
    public void ClearDetachesEveryChild()
    {
        var root = new RootDataNode();
        var children = Enumerable.Range(0, 3)
            .Select(_ => new TagCompoundDataNode(new TagNodeCompound()))
            .ToList();
        foreach (var c in children)
            root.Nodes.Add(c);

        root.Nodes.Clear();

        Assert.Empty(root.Nodes);
        Assert.All(children, c => Assert.Null(c.Parent));
    }

    [Fact]
    public void AddingNullThrows()
    {
        var root = new RootDataNode();
        Assert.Throws<ArgumentNullException>(() => root.Nodes.Add(null!));
    }

    [Fact]
    public void ChangeCountTracksStructuralEdits()
    {
        var root = new RootDataNode();
        int before = root.Nodes.ChangeCount;

        root.Nodes.Add(new TagCompoundDataNode(new TagNodeCompound()));
        Assert.True(root.Nodes.ChangeCount > before);
    }
}

public class NaturalComparerTests
{
    [Theory]
    [InlineData("item2", "item10")]     // the whole point: numeric, not lexicographic
    [InlineData("item2", "item11")]
    [InlineData("chunk9", "chunk10")]
    [InlineData("a", "b")]
    public void OrdersEmbeddedNumbersNumerically(string smaller, string larger)
    {
        using var cmp = new NaturalComparer();
        Assert.True(cmp.Compare(smaller, larger) < 0, $"expected '{smaller}' < '{larger}'");
        Assert.True(cmp.Compare(larger, smaller) > 0);
    }

    [Fact]
    public void EqualStringsCompareEqual()
    {
        using var cmp = new NaturalComparer();
        Assert.Equal(0, cmp.Compare("item2", "item2"));
    }

    [Fact]
    public void HandlesNegativeNumbers()
    {
        using var cmp = new NaturalComparer();
        Assert.True(cmp.Compare("r.-2.0", "r.-1.0") < 0);
    }

    [Fact]
    public void SortsARealisticRegionFileListing()
    {
        using var cmp = new NaturalComparer();
        var names = new List<string> { "r.10.0.mca", "r.2.0.mca", "r.1.0.mca" };
        names.Sort(cmp);
        Assert.Equal(["r.1.0.mca", "r.2.0.mca", "r.10.0.mca"], names);
    }
}

public class ClipboardDataTests
{
    /// <summary>
    /// NbtClipboardData already serialises to a byte[] via NbtTree, which is fully portable —
    /// unlike the WinForms clipboard controller's BinaryFormatter payload, which is removed in
    /// modern .NET. The Avalonia clipboard implementation carries these bytes under a custom
    /// format string, so this round-trip is the whole contract.
    /// </summary>
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
    [InlineData(TagType.TAG_COMPOUND)]
    public void EveryTagTypeRoundTrips(TagType type)
    {
        TagNode original = TagDataNode.DefaultTag(type);

        byte[] bytes = NbtClipboardData.SerializeNode(original);
        TagNode? restored = NbtClipboardData.DeserializeNode(bytes);

        Assert.NotNull(restored);
        Assert.Equal(type, restored!.GetTagType());
    }

    [Fact]
    public void ValuesSurviveTheRoundTripNotJustTypes()
    {
        var original = new TagNodeCompound {
            ["Name"] = new TagNodeString("Ünïcödé — строка"),
            ["Count"] = new TagNodeInt(-42),
            ["Blob"] = new TagNodeByteArray([1, 2, 3, 250]),
        };

        var restored = NbtClipboardData.DeserializeNode(NbtClipboardData.SerializeNode(original))!
                       .ToTagCompound();

        Assert.Equal("Ünïcödé — строка", restored["Name"].ToTagString().Data);
        Assert.Equal(-42, restored["Count"].ToTagInt().Data);
        Assert.Equal([(byte)1, 2, 3, 250], restored["Blob"].ToTagByteArray().Data);
    }

    [Fact]
    public void ListsAndNestingSurvive()
    {
        var list = new TagNodeList(TagType.TAG_COMPOUND);
        list.Add(new TagNodeCompound { ["id"] = new TagNodeString("first") });
        list.Add(new TagNodeCompound { ["id"] = new TagNodeString("second") });

        var restored = NbtClipboardData.DeserializeNode(NbtClipboardData.SerializeNode(list))!
                       .ToTagList();

        Assert.Equal(2, restored.Count);
        Assert.Equal("second", restored[1].ToTagCompound()["id"].ToTagString().Data);
    }

    [Fact]
    public void GarbageBytesYieldNullRatherThanThrowing()
    {
        // The clipboard can contain anything, so the Avalonia controller will hand whatever it
        // finds to DeserializeNode. A leading byte that is not a valid tag type produces a null
        // root, and DeserializeNode returns null — no exception. Callers must null-check.
        Assert.Null(NbtClipboardData.DeserializeNode([0xFF, 0xFE, 0xFD]));
    }

    [Fact]
    public void TruncatedPayloadThrowsRatherThanReturningAPartialTag()
    {
        // Silently returning half a tag would be far worse than throwing: it would paste
        // corrupted data into someone's world.
        byte[] full = NbtClipboardData.SerializeNode(new TagNodeString("a reasonably long value"));
        byte[] truncated = full[..(full.Length / 2)];

        Assert.ThrowsAny<Exception>(() => NbtClipboardData.DeserializeNode(truncated));
    }

    [Fact]
    public void PayloadWithoutTheRootKeyYieldsNull()
    {
        // SerializeNode wraps the tag in a compound under the key "root". A well-formed NBT
        // payload that did not come from us must not be mistaken for a copied tag.
        using var ms = new MemoryStream();
        new NbtTree(new TagNodeCompound { ["something-else"] = new TagNodeInt(1) }).WriteTo(ms);

        Assert.Null(NbtClipboardData.DeserializeNode(ms.ToArray()));
    }
}
