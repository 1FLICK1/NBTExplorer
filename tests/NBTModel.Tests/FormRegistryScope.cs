using NBTModel.Interop;
using Substrate.Nbt;

namespace NBTModel.Tests;

/// <summary>
/// FormRegistry is a static service locator holding synchronous delegates the model calls to pop
/// dialogs. That design is what lets the model stay toolkit-free — and it is also what makes the
/// interactive code paths testable headlessly. This scope installs stubs and restores whatever
/// was there before, so tests do not leak handlers into each other.
/// </summary>
public sealed class FormRegistryScope : IDisposable
{
    private readonly FormRegistry.EditStringAction? _editString;
    private readonly FormRegistry.EditRestrictedStringAction? _renameTag;
    private readonly FormRegistry.EditTagScalarAction? _editTagScalar;
    private readonly FormRegistry.EditByteArrayAction? _editByteArray;
    private readonly FormRegistry.CreateNodeAction? _createNode;
    private readonly Action<string>? _messageBox;

    private FormRegistryScope()
    {
        _editString = FormRegistry.EditString;
        _renameTag = FormRegistry.RenameTag;
        _editTagScalar = FormRegistry.EditTagScalar;
        _editByteArray = FormRegistry.EditByteArray;
        _createNode = FormRegistry.CreateNode;
        _messageBox = FormRegistry.MessageBox;
    }

    /// <summary>
    /// Stubs the scalar editor so it writes <paramref name="value"/> into the tag it is handed.
    /// Note the model passes the raw Substrate TagNode and expects it mutated IN PLACE — the
    /// DataNode is never told what changed. That is exactly why the Avalonia port wraps DataNode
    /// in a ViewModel that re-reads on demand instead of relying on change notification.
    /// </summary>
    public static FormRegistryScope EditScalarTo(TagNode value)
    {
        var scope = new FormRegistryScope();
        FormRegistry.EditTagScalar = data => {
            CopyInto(data.Tag, value);
            return true;
        };
        return scope;
    }

    public static FormRegistryScope EditStringTo(string value)
    {
        var scope = new FormRegistryScope();
        FormRegistry.EditString = data => { data.Value = value; return true; };
        return scope;
    }

    public static FormRegistryScope RenameTo(string value)
    {
        var scope = new FormRegistryScope();
        FormRegistry.EditString ??= _ => true;   // TagDataNode.RenameNode guards on EditString
        FormRegistry.RenameTag = data => { data.Value = value; return true; };
        return scope;
    }

    public static FormRegistryScope CreateTag(string name, TagNode node)
    {
        var scope = new FormRegistryScope();
        FormRegistry.CreateNode = data => {
            data.TagName = name;
            data.TagNode = node;
            return true;
        };
        return scope;
    }

    /// <summary>Every handler declines, as if the user pressed Cancel.</summary>
    public static FormRegistryScope CancelEverything()
    {
        var scope = new FormRegistryScope();
        FormRegistry.EditString = _ => false;
        FormRegistry.RenameTag = _ => false;
        FormRegistry.EditTagScalar = _ => false;
        FormRegistry.EditByteArray = _ => false;
        FormRegistry.CreateNode = _ => false;
        return scope;
    }

    private static void CopyInto(TagNode target, TagNode value)
    {
        switch (target) {
            case TagNodeByte b: b.Data = value.ToTagByte().Data; break;
            case TagNodeShort s: s.Data = value.ToTagShort().Data; break;
            case TagNodeInt i: i.Data = value.ToTagInt().Data; break;
            case TagNodeLong l: l.Data = value.ToTagLong().Data; break;
            case TagNodeFloat f: f.Data = value.ToTagFloat().Data; break;
            case TagNodeDouble d: d.Data = value.ToTagDouble().Data; break;
            case TagNodeString str: str.Data = value.ToTagString().Data; break;
            default: throw new NotSupportedException($"No in-place copy for {target.GetTagType()}");
        }
    }

    public void Dispose()
    {
        FormRegistry.EditString = _editString;
        FormRegistry.RenameTag = _renameTag;
        FormRegistry.EditTagScalar = _editTagScalar;
        FormRegistry.EditByteArray = _editByteArray;
        FormRegistry.CreateNode = _createNode;
        FormRegistry.MessageBox = _messageBox;
    }
}
