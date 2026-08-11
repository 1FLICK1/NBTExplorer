using NBTExplorer.Avalonia.Services;
using NBTExplorer.Avalonia.ViewModels;
using NBTExplorer.Model;
using NBTModel.Interop;
using Substrate.Core;
using Substrate.Nbt;
using Xunit;

namespace NBTExplorer.Avalonia.Tests;

/// <summary>
/// End-to-end coverage of the editing commands: open a real file, run the command the toolbar
/// runs, save, reload from disk, assert the bytes changed the way they should.
///
/// Saving is destructive and has no undo, so this is the layer that most needs proof. The
/// dialogs are stubbed through FormRegistry, which is the same seam the Avalonia handlers plug
/// into — so everything below the dialog is the production path.
/// </summary>
public class EditingCommandsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly ExplorerViewModel _vm;
    private FormRegistryStub _forms = null!;

    public EditingCommandsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "NBTEditTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "level.dat");

        var root = new TagNodeCompound {
            ["Data"] = new TagNodeCompound {
                ["LevelName"] = new TagNodeString("Probe World"),
                ["SpawnX"] = new TagNodeInt(128),
                ["Hardcore"] = new TagNodeByte(0),
                ["BorderSize"] = new TagNodeDouble(60000000.0),
            },
        };
        using (Stream s = new NBTFile(_path).GetDataOutputStream(CompressionType.GZip))
            new NbtTree(root, "").WriteTo(s);

        _vm = new ExplorerViewModel(new IconMap());
        _forms = new FormRegistryStub();
        _vm.OpenPaths([_path]);
    }

    public void Dispose()
    {
        _forms.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>Navigate into level.dat → Data, which is where the interesting tags live.</summary>
    private void GoToData()
    {
        var file = _vm.Roots.Single();
        _vm.Navigate(file);
        var data = _vm.Items.Single(i => i.Name == "Data");
        _vm.Navigate(data);
    }

    private void Select(string name)
    {
        _vm.Selection.Clear();
        _vm.Selection.Add(_vm.Items.Single(i => i.Name == name));
    }

    private static TagNodeCompound ReloadData(string path)
    {
        var tree = new NbtTree();
        tree.ReadFrom(new NBTFile(path).GetDataInputStream(CompressionType.GZip));
        return tree.Root["Data"].ToTagCompound();
    }

    // ---- opening and navigation --------------------------------------------------------------

    [Fact]
    public void OpeningAFileLandsInItAndListsItsContents()
    {
        Assert.True(_vm.HasOpenNodes);
        Assert.Single(_vm.Roots);
        Assert.Contains(_vm.Items, i => i.Name == "Data");
    }

    [Fact]
    public void NavigatingIntoAContainerListsItsChildrenWithTypesAndValues()
    {
        GoToData();

        var spawn = _vm.Items.Single(i => i.Name == "SpawnX");
        Assert.Equal("Int", spawn.TypeName);
        Assert.Equal("128", spawn.ValueText);

        var name = _vm.Items.Single(i => i.Name == "LevelName");
        Assert.Equal("String", name.TypeName);
        Assert.Equal("Probe World", name.ValueText);
    }

    [Fact]
    public void UpAndBackReturnToThePreviousContainer()
    {
        var file = _vm.Roots.Single();
        GoToData();
        Assert.True(_vm.CanGoUp);

        _vm.GoUpCommand.Execute(null);
        Assert.Same(file, _vm.CurrentFolder);

        Assert.True(_vm.CanGoBack);
        _vm.GoBackCommand.Execute(null);
        Assert.Equal("Data", _vm.CurrentFolder!.Name);
    }

    // ---- editing -----------------------------------------------------------------------------

    [Fact]
    public void EditingAScalarPersistsAfterSaveAndReload()
    {
        GoToData();
        Select("SpawnX");
        Assert.True(_vm.CanEdit);

        _forms.ScalarValue = "999";
        _vm.EditCommand.Execute(null);

        // Visible in the list immediately, without a manual refresh.
        Assert.Equal("999", _vm.Items.Single(i => i.Name == "SpawnX").ValueText);
        Assert.True(_vm.IsAnyModified);

        _vm.SaveCommand.Execute(null);
        Assert.False(_vm.IsAnyModified);

        var data = ReloadData(_path);
        Assert.Equal(999, data["SpawnX"].ToTagInt().Data);
        // Untouched siblings must survive intact.
        Assert.Equal("Probe World", data["LevelName"].ToTagString().Data);
    }

    [Fact]
    public void EditingAStringPersists()
    {
        GoToData();
        Select("LevelName");

        _forms.StringValue = "Renamed World";
        _vm.EditCommand.Execute(null);
        _vm.SaveCommand.Execute(null);

        Assert.Equal("Renamed World", ReloadData(_path)["LevelName"].ToTagString().Data);
    }

    [Fact]
    public void CancellingAnEditLeavesTheFileClean()
    {
        GoToData();
        Select("SpawnX");

        _forms.Cancel = true;
        _vm.EditCommand.Execute(null);

        Assert.False(_vm.IsAnyModified);
        Assert.Equal("128", _vm.Items.Single(i => i.Name == "SpawnX").ValueText);
    }

    [Fact]
    public void RenamingATagPersists()
    {
        GoToData();
        Select("SpawnX");
        Assert.True(_vm.CanRename);

        _forms.RenameValue = "SpawnXX";
        _vm.RenameCommand.Execute(null);
        _vm.SaveCommand.Execute(null);

        var data = ReloadData(_path);
        Assert.True(data.ContainsKey("SpawnXX"));
        Assert.False(data.ContainsKey("SpawnX"));
    }

    [Fact]
    public void DeletingATagPersists()
    {
        GoToData();
        Select("Hardcore");
        Assert.True(_vm.CanDelete);

        _vm.DeleteCommand.Execute(null);

        Assert.DoesNotContain(_vm.Items, i => i.Name == "Hardcore");
        _vm.SaveCommand.Execute(null);

        Assert.False(ReloadData(_path).ContainsKey("Hardcore"));
    }

    [Fact]
    public void CreatingATagPersists()
    {
        GoToData();
        Assert.Contains(TagType.TAG_INT, _vm.CreatableTypes);

        _forms.NewTagName = "NewCount";
        _vm.CreateTagCommand.Execute(TagType.TAG_INT);

        Assert.Contains(_vm.Items, i => i.Name == "NewCount");
        _vm.SaveCommand.Execute(null);

        var data = ReloadData(_path);
        Assert.True(data.ContainsKey("NewCount"));
        Assert.Equal(TagType.TAG_INT, data["NewCount"].GetTagType());
    }

    [Fact]
    public void CopyAndPasteDuplicatesATagUnderANonClashingName()
    {
        var clipboard = new FakeClipboard();
        NbtClipboardController.Initialize(clipboard);

        GoToData();
        Select("SpawnX");
        Assert.True(_vm.CanCopy);
        _vm.CopyCommand.Execute(null);

        Assert.True(_vm.CanPaste);
        _vm.PasteCommand.Execute(null);
        _vm.SaveCommand.Execute(null);

        var data = ReloadData(_path);
        // NbtFileDataNode.MakeUniqueName appends " (Copy N)" rather than overwriting.
        Assert.True(data.ContainsKey("SpawnX"));
        Assert.Contains(data.Keys, k => k != "SpawnX" && k.StartsWith("SpawnX"));
    }

    [Fact]
    public void CommandsAreDisabledWhenTheSelectionCannotSupportThem()
    {
        GoToData();

        // A compound has no scalar value to edit.
        _vm.Selection.Clear();
        _vm.Navigate(_vm.Roots.Single());
        Select("Data");
        Assert.False(_vm.CanEdit);

        // Compound children are unordered, so reordering is meaningless.
        Assert.False(_vm.CanMoveUp);
        Assert.False(_vm.CanMoveDown);
    }

    [Fact]
    public void SaveIsOfferedOnlyWhileSomethingIsDirty()
    {
        Assert.False(_vm.CanSave);

        GoToData();
        Select("SpawnX");
        _forms.ScalarValue = "7";
        _vm.EditCommand.Execute(null);

        Assert.True(_vm.CanSave);
        _vm.SaveCommand.Execute(null);
        Assert.False(_vm.CanSave);
    }

    [Fact]
    public void ListChildrenCanBeReorderedAndThePositionPersists()
    {
        // Build a file whose root holds an ordered list.
        string path = Path.Combine(_dir, "list.dat");
        var list = new TagNodeList(TagType.TAG_STRING);
        list.Add(new TagNodeString("first"));
        list.Add(new TagNodeString("second"));
        list.Add(new TagNodeString("third"));
        using (Stream s = new NBTFile(path).GetDataOutputStream(CompressionType.GZip))
            new NbtTree(new TagNodeCompound { ["Items"] = list }, "").WriteTo(s);

        var vm = new ExplorerViewModel(new IconMap());
        vm.OpenPaths([path]);
        vm.Navigate(vm.Roots.Single());
        vm.Navigate(vm.Items.Single(i => i.Name == "Items"));

        Assert.Equal(["first", "second", "third"], vm.Items.Select(i => i.ValueText));

        vm.Selection.Clear();
        vm.Selection.Add(vm.Items[2]);
        Assert.True(vm.CanMoveUp);
        vm.MoveUpCommand.Execute(null);

        Assert.Equal(["first", "third", "second"], vm.Items.Select(i => i.ValueText));

        vm.SaveCommand.Execute(null);

        var tree = new NbtTree();
        tree.ReadFrom(new NBTFile(path).GetDataInputStream(CompressionType.GZip));
        var saved = tree.Root["Items"].ToTagList();
        Assert.Equal(["first", "third", "second"],
                     saved.Select(t => t.ToTagString().Data));
    }

    // ---- stubs -------------------------------------------------------------------------------

    /// <summary>
    /// Stands in for the Avalonia dialogs. FormRegistry's service-locator design is what makes
    /// this possible — the model calls these delegates without knowing what is behind them.
    /// </summary>
    private sealed class FormRegistryStub : IDisposable
    {
        public string ScalarValue = "0";
        public string StringValue = "";
        public string RenameValue = "renamed";
        public string NewTagName = "NewTag";
        public bool Cancel;

        public FormRegistryStub()
        {
            FormRegistry.EditTagScalar = data => {
                if (Cancel) return false;
                return TagValues.TryApply(data.Tag, Value(data.Tag));
            };
            FormRegistry.EditString = data => {
                if (Cancel) return false;
                data.Value = StringValue;
                return true;
            };
            FormRegistry.RenameTag = data => {
                if (Cancel) return false;
                data.Value = RenameValue;
                return true;
            };
            FormRegistry.CreateNode = data => {
                if (Cancel) return false;
                data.TagName = NewTagName;
                data.TagNode = TagValues.Default(data.TagType);
                return true;
            };
            FormRegistry.EditByteArray = _ => !Cancel;
            FormRegistry.MessageBox = _ => { };
        }

        private string Value(TagNode tag)
            => tag.GetTagType() == TagType.TAG_STRING ? StringValue : ScalarValue;

        public void Dispose()
        {
            FormRegistry.EditTagScalar = null;
            FormRegistry.EditString = null;
            FormRegistry.RenameTag = null;
            FormRegistry.CreateNode = null;
            FormRegistry.EditByteArray = null;
            FormRegistry.MessageBox = null;
        }
    }

    /// <summary>
    /// In-memory clipboard mirroring the Avalonia one: the serialised node plus the tag name,
    /// which SerializeNode does not encode. Losing the name makes every paste land as "UNNAMED".
    /// </summary>
    private sealed class FakeClipboard : INbtClipboardController
    {
        private byte[]? _payload;
        private string? _name;

        public bool ContainsData => _payload is not null;

        public void CopyToClipboard(NbtClipboardData data)
        {
            _payload = NbtClipboardData.SerializeNode(data.Node);
            _name = data.Name;
        }

        public NbtClipboardData CopyFromClipboard()
            => _payload is null
                ? null!
                : new NbtClipboardData(_name, NbtClipboardData.DeserializeNode(_payload));
    }
}
