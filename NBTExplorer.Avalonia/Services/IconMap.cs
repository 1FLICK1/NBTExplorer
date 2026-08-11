using NBTExplorer.Model;

namespace NBTExplorer.Avalonia.Services;

/// <summary>
/// Maps a DataNode type to a vector icon resource key and an accent brush key.
///
/// Replaces Windows\IconRegistry.cs (Type → ImageList index). The old raster set is unusable
/// here: it lives as a BinaryFormatter-serialised ImageList inside MainForm.resx, blurs at
/// 150%/200% DPI, and its dark-on-light glyphs vanish in dark theme. The old Type→int mapping
/// was also duplicated in three places (NodeTreeController.cs:744, MainForm.cs:136,
/// RuleTreeController.cs:28) — this is the single source of truth.
///
/// Note the deliberate consolidation: the old set used SEVEN near-identical document-attribute-*
/// glyphs (b/d/f/i/l/s) differing only by a tiny letter, unreadable at any size. Here there are
/// four shapes (integer / float / string / array) distinguished by COLOUR, which reads far
/// better — see Assets\Themes\Colors.axaml.
/// </summary>
public interface IIconMap
{
    string IconKey(DataNode node);
    string BrushKey(DataNode node);
}

public sealed class IconMap : IIconMap
{
    private const string DefaultIcon = "Icon.Unknown";
    private const string DefaultBrush = "TagNeutralBrush";

    private static readonly Dictionary<Type, string> Icons = new() {
        // Integral scalars share one shape and one colour.
        [typeof(TagByteDataNode)] = "Icon.TagInt",
        [typeof(TagShortDataNode)] = "Icon.TagInt",
        [typeof(TagIntDataNode)] = "Icon.TagInt",
        [typeof(TagLongDataNode)] = "Icon.TagInt",

        [typeof(TagFloatDataNode)] = "Icon.TagFloat",
        [typeof(TagDoubleDataNode)] = "Icon.TagFloat",

        [typeof(TagStringDataNode)] = "Icon.TagString",

        [typeof(TagByteArrayDataNode)] = "Icon.TagArray",
        [typeof(TagIntArrayDataNode)] = "Icon.TagArray",
        [typeof(TagShortArrayDataNode)] = "Icon.TagArray",
        [typeof(TagLongArrayDataNode)] = "Icon.TagArray",

        [typeof(TagListDataNode)] = "Icon.TagList",
        [typeof(TagCompoundDataNode)] = "Icon.TagCompound",

        [typeof(RegionChunkDataNode)] = "Icon.Chunk",
        [typeof(RegionFileDataNode)] = "Icon.Region",
        [typeof(CubicRegionDataNode)] = "Icon.Region",
        [typeof(DirectoryDataNode)] = "Icon.Folder",
        [typeof(NbtFileDataNode)] = "Icon.File",
        [typeof(RootDataNode)] = "Icon.Root",
    };

    private static readonly Dictionary<Type, string> Brushes = new() {
        [typeof(TagByteDataNode)] = "TagIntBrush",
        [typeof(TagShortDataNode)] = "TagIntBrush",
        [typeof(TagIntDataNode)] = "TagIntBrush",
        [typeof(TagLongDataNode)] = "TagIntBrush",

        [typeof(TagFloatDataNode)] = "TagFloatBrush",
        [typeof(TagDoubleDataNode)] = "TagFloatBrush",

        [typeof(TagStringDataNode)] = "TagStringBrush",

        [typeof(TagByteArrayDataNode)] = "TagArrayBrush",
        [typeof(TagIntArrayDataNode)] = "TagArrayBrush",
        [typeof(TagShortArrayDataNode)] = "TagArrayBrush",
        [typeof(TagLongArrayDataNode)] = "TagArrayBrush",

        [typeof(TagListDataNode)] = "TagContainerBrush",
        [typeof(TagCompoundDataNode)] = "TagContainerBrush",

        [typeof(RegionChunkDataNode)] = "TagContainerBrush",
        [typeof(RegionFileDataNode)] = "TagFileBrush",
        [typeof(CubicRegionDataNode)] = "TagFileBrush",
        [typeof(DirectoryDataNode)] = "TagFolderBrush",
        [typeof(NbtFileDataNode)] = "TagFileBrush",
        [typeof(RootDataNode)] = "TagFolderBrush",
    };

    public string IconKey(DataNode node) => Lookup(Icons, node, DefaultIcon);

    public string BrushKey(DataNode node) => Lookup(Brushes, node, DefaultBrush);

    private static string Lookup(Dictionary<Type, string> map, DataNode node, string fallback)
    {
        // Walk the type hierarchy so a future DataNode subclass inherits its parent's icon
        // instead of silently falling back to the question mark.
        for (Type? t = node.GetType(); t is not null; t = t.BaseType) {
            if (map.TryGetValue(t, out string? key))
                return key;
        }
        return fallback;
    }
}
