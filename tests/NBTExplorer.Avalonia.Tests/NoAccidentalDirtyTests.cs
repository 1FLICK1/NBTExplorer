using NBTExplorer.Avalonia.Services;
using NBTExplorer.Avalonia.ViewModels;
using NBTModel.Interop;
using Substrate.Core;
using Substrate.Nbt;
using Xunit;

namespace NBTExplorer.Avalonia.Tests;

/// <summary>
/// "Unsaved changes" must mean the user actually changed something. Browsing, expanding,
/// collapsing, or opening an editor and confirming the same value must all leave the file clean —
/// otherwise the close prompt cries wolf and people learn to click through it.
/// </summary>
public class NoAccidentalDirtyTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public NoAccidentalDirtyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "NBTDirty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "level.dat");

        var list = new TagNodeList(TagType.TAG_STRING);
        list.Add(new TagNodeString("a"));
        list.Add(new TagNodeString("b"));

        var root = new TagNodeCompound {
            ["Data"] = new TagNodeCompound {
                ["LevelName"] = new TagNodeString("World"),
                ["SpawnX"] = new TagNodeInt(128),
                ["Blob"] = new TagNodeByteArray([1, 2, 3, 4]),
                ["Nested"] = new TagNodeCompound { ["Deep"] = new TagNodeInt(1) },
                ["Items"] = list,
            },
        };
        using Stream s = new NBTFile(_path).GetDataOutputStream(CompressionType.GZip);
        new NbtTree(root, "").WriteTo(s);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private ExplorerViewModel Open()
    {
        var vm = new ExplorerViewModel(new IconMap());
        vm.OpenPaths([_path]);
        return vm;
    }

    [Fact]
    public void OpeningAFileDoesNotMarkItModified()
    {
        Assert.False(Open().IsAnyModified);
    }

    [Fact]
    public void BrowsingTheWholeTreeDoesNotMarkAnythingModified()
    {
        var vm = Open();
        VisitEverything(vm, vm.Roots.Single());
        Assert.False(vm.IsAnyModified);
    }

    [Fact]
    public void ExpandingAndCollapsingRepeatedlyDoesNotMarkAnythingModified()
    {
        var vm = Open();
        var file = vm.Roots.Single();

        for (int i = 0; i < 3; i++) {
            file.IsExpanded = true;
            foreach (var child in file.Children.Where(c => !c.IsPlaceholder).ToList())
                child.IsExpanded = true;
            file.IsExpanded = false;
        }

        Assert.False(vm.IsAnyModified);
    }

    [Theory]
    [InlineData("SpawnX")]     // numeric scalar → FormRegistry.EditTagScalar
    [InlineData("LevelName")]  // string        → FormRegistry.EditString
    public void ConfirmingAnEditorWithoutChangingTheValueLeavesTheFileClean(string tagName)
    {
        var vm = Open();
        using var _ = new EchoBackHandlers();

        vm.Navigate(vm.Roots.Single());
        vm.Navigate(vm.Items.Single(i => i.Name == "Data"));
        vm.Selection.Clear();
        vm.Selection.Add(vm.Items.Single(i => i.Name == tagName));

        vm.EditCommand.Execute(null);

        Assert.False(vm.IsAnyModified);
    }

    [Fact]
    public void RenamingATagToItsOwnNameLeavesTheFileClean()
    {
        var vm = Open();
        using var _ = new EchoBackHandlers();

        vm.Navigate(vm.Roots.Single());
        vm.Navigate(vm.Items.Single(i => i.Name == "Data"));
        vm.Selection.Clear();
        vm.Selection.Add(vm.Items.Single(i => i.Name == "SpawnX"));

        vm.RenameCommand.Execute(null);

        Assert.False(vm.IsAnyModified);
    }

    [Fact]
    public void CancellingEveryEditorLeavesTheFileClean()
    {
        var vm = Open();
        using var _ = new CancellingHandlers();

        vm.Navigate(vm.Roots.Single());
        vm.Navigate(vm.Items.Single(i => i.Name == "Data"));

        foreach (var item in vm.Items.ToList()) {
            vm.Selection.Clear();
            vm.Selection.Add(item);
            vm.EditCommand.Execute(null);
            vm.RenameCommand.Execute(null);
        }

        Assert.False(vm.IsAnyModified);
    }

    private static void VisitEverything(ExplorerViewModel vm, NodeViewModel node)
    {
        vm.Navigate(node);
        foreach (var child in vm.Items.Where(i => i.IsContainer).ToList())
            VisitEverything(vm, child);
    }

    /// <summary>
    /// Stands in for a user who opens the editor and presses OK without typing anything. The real
    /// AvaloniaFormHandlers compare against the original and return false; these stubs reproduce
    /// that contract so the ViewModel layer is tested against the same behaviour.
    /// </summary>
    private sealed class EchoBackHandlers : IDisposable
    {
        public EchoBackHandlers()
        {
            FormRegistry.EditTagScalar = _ => false;   // value unchanged → no edit
            FormRegistry.EditString = _ => false;
            FormRegistry.RenameTag = _ => false;
            FormRegistry.EditByteArray = _ => false;
            FormRegistry.MessageBox = _ => { };
        }

        public void Dispose() => ClearHandlers();
    }

    private sealed class CancellingHandlers : IDisposable
    {
        public CancellingHandlers()
        {
            FormRegistry.EditTagScalar = _ => false;
            FormRegistry.EditString = _ => false;
            FormRegistry.RenameTag = _ => false;
            FormRegistry.EditByteArray = _ => false;
            FormRegistry.CreateNode = _ => false;
            FormRegistry.MessageBox = _ => { };
        }

        public void Dispose() => ClearHandlers();
    }

    private static void ClearHandlers()
    {
        FormRegistry.EditTagScalar = null;
        FormRegistry.EditString = null;
        FormRegistry.RenameTag = null;
        FormRegistry.EditByteArray = null;
        FormRegistry.CreateNode = null;
        FormRegistry.MessageBox = null;
    }
}
