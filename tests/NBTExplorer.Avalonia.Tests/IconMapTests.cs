using System.Xml.Linq;
using NBTExplorer.Avalonia.Services;
using NBTExplorer.Model;
using Substrate.Nbt;
using Xunit;

namespace NBTExplorer.Avalonia.Tests;

public class IconMapTests
{
    private static readonly IconMap Map = new();

    [Theory]
    [InlineData(TagType.TAG_BYTE, "Icon.TagInt", "TagIntBrush")]
    [InlineData(TagType.TAG_SHORT, "Icon.TagInt", "TagIntBrush")]
    [InlineData(TagType.TAG_INT, "Icon.TagInt", "TagIntBrush")]
    [InlineData(TagType.TAG_LONG, "Icon.TagInt", "TagIntBrush")]
    [InlineData(TagType.TAG_FLOAT, "Icon.TagFloat", "TagFloatBrush")]
    [InlineData(TagType.TAG_DOUBLE, "Icon.TagFloat", "TagFloatBrush")]
    [InlineData(TagType.TAG_STRING, "Icon.TagString", "TagStringBrush")]
    [InlineData(TagType.TAG_BYTE_ARRAY, "Icon.TagArray", "TagArrayBrush")]
    [InlineData(TagType.TAG_INT_ARRAY, "Icon.TagArray", "TagArrayBrush")]
    [InlineData(TagType.TAG_SHORT_ARRAY, "Icon.TagArray", "TagArrayBrush")]
    [InlineData(TagType.TAG_LONG_ARRAY, "Icon.TagArray", "TagArrayBrush")]
    [InlineData(TagType.TAG_LIST, "Icon.TagList", "TagContainerBrush")]
    [InlineData(TagType.TAG_COMPOUND, "Icon.TagCompound", "TagContainerBrush")]
    public void EveryTagTypeHasAnIconAndAnAccent(TagType type, string icon, string brush)
    {
        var node = TagDataNode.CreateFromTag(TagDataNode.DefaultTag(type))!;

        Assert.Equal(icon, Map.IconKey(node));
        Assert.Equal(brush, Map.BrushKey(node));
    }

    [Fact]
    public void FileAndFolderNodesAreCovered()
    {
        Assert.Equal("Icon.Folder", Map.IconKey(new DirectoryDataNode(@"C:\worlds")));
        Assert.Equal("Icon.Region", Map.IconKey(RegionFileDataNode.TryCreateFrom("r.0.0.mca")));
        Assert.Equal("Icon.Root", Map.IconKey(new RootDataNode()));
    }

    [Fact]
    public void RootDataNodeDoesNotInheritTheCompoundIcon()
    {
        // RootDataNode derives from TagCompoundDataNode, so the hierarchy walk must find the
        // exact match first rather than falling through to the base type.
        Assert.NotEqual(Map.IconKey(new TagCompoundDataNode(new TagNodeCompound())),
                        Map.IconKey(new RootDataNode()));
    }

    [Fact]
    public void UnknownNodeTypeFallsBackInsteadOfThrowing()
    {
        Assert.Equal("Icon.Unknown", Map.IconKey(new DataNode()));
        Assert.Equal("TagNeutralBrush", Map.BrushKey(new DataNode()));
    }

    /// <summary>
    /// Every key IconMap can emit must exist in the resource dictionaries, or the icon silently
    /// renders blank at runtime with no error anywhere.
    /// </summary>
    [Fact]
    public void EveryEmittedKeyExistsInTheResourceDictionaries()
    {
        var declared = ResourceKeys("Assets/Icons.axaml")
            .Concat(ResourceKeys("Assets/Themes/Colors.axaml"))
            .ToHashSet();

        var emitted = new List<string>();
        foreach (TagType type in Enum.GetValues<TagType>()) {
            if (type == TagType.TAG_END)
                continue;
            var tag = TagDataNode.DefaultTag(type);
            if (TagDataNode.CreateFromTag(tag) is not { } node)
                continue;
            emitted.Add(Map.IconKey(node));
            emitted.Add(Map.BrushKey(node));
        }

        foreach (DataNode node in new DataNode[] {
                     new DirectoryDataNode(@"C:\x"), new RootDataNode(), new DataNode(),
                     RegionFileDataNode.TryCreateFrom("r.0.0.mca"),
                 }) {
            emitted.Add(Map.IconKey(node));
            emitted.Add(Map.BrushKey(node));
        }

        var missing = emitted.Distinct().Where(k => !declared.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Keys not declared in resources: " + string.Join(", ", missing));
    }

    private static IEnumerable<string> ResourceKeys(string relativePath)
    {
        // The .axaml files ship as AvaloniaResource, not as content beside the test assembly, so
        // read them from the source tree.
        string root = FindRepoRoot();
        string path = Path.Combine(root, "NBTExplorer.Avalonia", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"missing {path}");

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path)
            .Descendants()
            .Select(e => e.Attribute(x + "Key")?.Value)
            .Where(v => v is not null)!;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NBTExplorer.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
