using NBTExplorer.Model;
using NBTModel.Interop;
using Substrate.Core;
using Substrate.Nbt;
using Xunit;

namespace NBTModel.Tests;

/// <summary>
/// Open → expand → edit → save → reload, through the DataNode layer rather than raw Substrate.
/// This is the operation NBTExplorer exists to perform, and the one where a regression silently
/// corrupts someone's world.
/// </summary>
public class NbtFileRoundTripTests
{
    [Theory]
    [InlineData(CompressionType.GZip)]    // level.dat
    [InlineData(CompressionType.None)]    // idcounts.dat
    public void EditedScalarSurvivesSaveAndReload(CompressionType compression)
    {
        using var dir = new TempDir();
        string path = dir.File("level.dat");
        WriteNbt(path, compression, SampleRoot(), "");

        // FormRegistry is a static service locator, so tests drive interactive model paths
        // headlessly by registering stubs — the same seam the Avalonia front-end will use.
        using var _ = FormRegistryScope.EditScalarTo(new TagNodeInt(999));

        var file = NbtFileDataNode.TryCreateFrom(path);
        Assert.NotNull(file);
        file!.Expand();

        var data = Assert.IsType<TagCompoundDataNode>(Single(file, "Data"));
        data.Expand();

        var spawnX = Single(data, "SpawnX");
        Assert.True(spawnX.CanEditNode);
        Assert.True(spawnX.EditNode());
        Assert.True(file.IsModified);

        file.Save();
        Assert.False(file.IsModified);

        var reloaded = NbtFileDataNode.TryCreateFrom(path);
        Assert.NotNull(reloaded);
        reloaded!.Expand();
        var data2 = (TagCompoundDataNode)Single(reloaded, "Data");
        data2.Expand();

        Assert.Equal("SpawnX: 999", Single(data2, "SpawnX").NodeDisplay);
        // Untouched siblings must be byte-identical, not just present.
        Assert.Equal("LevelName: Probe World", Single(data2, "LevelName").NodeDisplay);
        Assert.Equal("RandomSeed: -6821066263748282474", Single(data2, "RandomSeed").NodeDisplay);
    }

    [Fact]
    public void TryCreateFromAutodetectsCompression()
    {
        using var dir = new TempDir();
        string gz = dir.File("gzipped.dat");
        string raw = dir.File("raw.dat");
        WriteNbt(gz, CompressionType.GZip, SampleRoot(), "");
        WriteNbt(raw, CompressionType.None, SampleRoot(), "");

        // TryCreateFrom tries GZip then falls back to None; the fallback only works because a
        // wrong-compression read throws rather than returning a bogus tree.
        Assert.NotNull(NbtFileDataNode.TryCreateFrom(gz));
        Assert.NotNull(NbtFileDataNode.TryCreateFrom(raw));
        Assert.Null(NbtFileDataNode.TryCreateFrom(dir.File("does-not-exist.dat")));
    }

    [Fact]
    public void NonNbtFileIsRejectedRatherThanThrowing()
    {
        using var dir = new TempDir();
        string path = dir.File("notes.dat");
        File.WriteAllText(path, "this is definitely not NBT");

        Assert.Null(NbtFileDataNode.TryCreateFrom(path));
    }

    [Fact]
    public void SupportedNamePatternMatchesTheDocumentedExtensions()
    {
        Assert.True(NbtFileDataNode.SupportedNamePattern("level.dat"));
        Assert.True(NbtFileDataNode.SupportedNamePattern("build.schematic"));
        Assert.True(NbtFileDataNode.SupportedNamePattern("level.dat_old"));
        Assert.True(NbtFileDataNode.SupportedNamePattern(@"C:\worlds\thing.nbt"));
        Assert.False(NbtFileDataNode.SupportedNamePattern("readme.txt"));
        Assert.False(NbtFileDataNode.SupportedNamePattern("r.0.0.mca"));
    }

    [Fact]
    public void RegionFileExpandsToItsChunksAndChunkEditsPersist()
    {
        using var dir = new TempDir();
        string path = dir.File("r.0.0.mca");

        (int x, int z)[] coords = [(0, 0), (5, 17), (31, 31)];
        var region = new RegionFile(path);
        foreach (var (x, z) in coords) {
            using Stream str = region.GetChunkDataOutputStream(x, z);
            new NbtTree(Chunk(x, z), "").WriteTo(str);
        }
        region.Close();

        Assert.True(RegionFileDataNode.SupportedNamePattern(path));
        var node = RegionFileDataNode.TryCreateFrom(path);
        node.Expand();
        Assert.Equal(coords.Length, node.Nodes.Count);

        var chunk = node.Nodes.OfType<RegionChunkDataNode>().Single(c => c.X == 5 && c.Z == 17);
        chunk.Expand();

        using var _ = FormRegistryScope.EditScalarTo(new TagNodeLong(1234567890123L));
        var lastUpdate = Single(chunk, "LastUpdate");
        Assert.True(lastUpdate.EditNode());

        // Dirty state must have climbed from the tag to the region file.
        Assert.True(node.IsModified);
        node.Save();
        Assert.False(node.IsModified);

        var reopened = RegionFileDataNode.TryCreateFrom(path);
        reopened.Expand();
        var chunk2 = reopened.Nodes.OfType<RegionChunkDataNode>().Single(c => c.X == 5 && c.Z == 17);
        chunk2.Expand();
        Assert.Equal("LastUpdate: 1234567890123", Single(chunk2, "LastUpdate").NodeDisplay);

        // Neighbours must be untouched.
        var corner = reopened.Nodes.OfType<RegionChunkDataNode>().Single(c => c.X == 31 && c.Z == 31);
        corner.Expand();
        Assert.Equal("xPos: 31", Single(corner, "xPos").NodeDisplay);
    }

    [Fact]
    public void RegionCoordinatesParsesNegativeValues()
    {
        Assert.True(RegionFileDataNode.RegionCoordinates("r.-3.7.mca", out int rx, out int rz));
        Assert.Equal(-3, rx);
        Assert.Equal(7, rz);
        Assert.False(RegionFileDataNode.RegionCoordinates("level.dat", out _, out _));
    }

    [Fact]
    public void ExpandThenReleaseThenExpandDoesNotDuplicateChildren()
    {
        using var dir = new TempDir();
        string path = dir.File("level.dat");
        WriteNbt(path, CompressionType.GZip, SampleRoot(), "");

        var file = NbtFileDataNode.TryCreateFrom(path)!;
        file.Expand();
        int first = file.Nodes.Count;

        file.Release();
        Assert.Empty(file.Nodes);

        file.Expand();
        Assert.Equal(first, file.Nodes.Count);
    }

    // ---- helpers ----------------------------------------------------------------------------

    internal static void WriteNbt(string path, CompressionType compression, TagNodeCompound root, string name)
    {
        using Stream str = new NBTFile(path).GetDataOutputStream(compression);
        new NbtTree(root, name).WriteTo(str);
    }

    internal static TagNodeCompound SampleRoot() => new() {
        ["Data"] = new TagNodeCompound {
            ["LevelName"] = new TagNodeString("Probe World"),
            ["SpawnX"] = new TagNodeInt(128),
            ["SpawnZ"] = new TagNodeInt(-256),
            ["RandomSeed"] = new TagNodeLong(-6821066263748282474L),
            ["BorderSize"] = new TagNodeDouble(59999968.0),
        },
    };

    private static TagNodeCompound Chunk(int x, int z) => new() {
        ["xPos"] = new TagNodeInt(x),
        ["zPos"] = new TagNodeInt(z),
        ["LastUpdate"] = new TagNodeLong(0),
    };

    internal static DataNode Single(DataNode parent, string name)
        => parent.Nodes.Single(n => n.NodeName == name);
}
