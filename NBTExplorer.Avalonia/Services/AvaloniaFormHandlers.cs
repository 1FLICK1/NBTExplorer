using Avalonia.Controls;
using Avalonia.Media;
using NBTExplorer.Avalonia.Views.Dialogs;
using NBTModel.Interop;
using Substrate.Nbt;

namespace NBTExplorer.Avalonia.Services;

/// <summary>
/// Avalonia implementation of the model's dialog seam — the counterpart of
/// NBTExplorer\Windows\FormHandlers.cs. Registering these five delegates is what lets NBTModel
/// pop editors without knowing which toolkit is running.
///
/// Every handler is synchronous because the delegates are: the model uses the bool return value
/// immediately to decide whether to commit. <see cref="UiThreadBlocking"/> bridges that to
/// Avalonia's async ShowDialog. See that class for why, and for the plan to remove it.
/// </summary>
public sealed class AvaloniaFormHandlers(Window owner, IIconMap icons)
{
    public void Register()
    {
        FormRegistry.EditString = EditString;
        FormRegistry.RenameTag = RenameTag;
        FormRegistry.EditTagScalar = EditTagScalar;
        FormRegistry.EditByteArray = EditByteArray;
        FormRegistry.CreateNode = CreateNode;
        FormRegistry.MessageBox = message => ShowDialog(MessageDialog.Info(message));
    }

    private bool EditString(StringFormData data)
    {
        string original = data.Value ?? "";

        var dialog = EditValueDialog.Create(
            "Edit text", "Value", original,
            validate: text => !data.AllowEmpty && string.IsNullOrEmpty(text)
                ? "The value cannot be empty."
                : null,
            multiline: true);

        if (ShowDialog<bool>(dialog) != true)
            return false;

        // TagStringDataNode.EditNode sets IsDataModified unconditionally on a true return, so an
        // unchanged value would leave the file marked dirty and prompt on close for nothing.
        if (dialog.Value == original)
            return false;

        data.Value = dialog.Value;
        return true;
    }

    private bool RenameTag(RestrictedStringFormData data)
    {
        string original = data.Value ?? "";

        var dialog = EditValueDialog.Create(
            "Rename", "Name", original,
            validate: text => {
                if (!data.AllowEmpty && string.IsNullOrEmpty(text))
                    return "The name cannot be empty.";
                // RestrictedValues holds every name already in the compound, including this
                // tag's own — renaming to itself has to stay legal.
                if (text != original && data.RestrictedValues.Contains(text))
                    return $"'{text}' is already used in this compound.";
                return null;
            });

        if (ShowDialog<bool>(dialog) != true)
            return false;

        // Same guard as EditString: renaming a tag to its current name is a no-op that would
        // otherwise mark the whole file modified.
        if (dialog.Value == original)
            return false;

        data.Value = dialog.Value;
        return true;
    }

    private bool EditTagScalar(TagScalarFormData data)
    {
        TagType type = data.Tag.GetTagType();
        string original = TagValues.Format(data.Tag);

        // Strings get the roomier multi-line editor; the WinForms app split these into
        // EditValue and EditString for the same reason.
        var dialog = EditValueDialog.Create(
            "Edit value", DescribeRange(type), original,
            validate: text => TagValues.Validate(type, text),
            multiline: type == TagType.TAG_STRING);

        if (ShowDialog<bool>(dialog) != true)
            return false;

        // Returning false for an unchanged value keeps the file from being marked dirty just
        // because the user opened the editor and pressed OK. The model sets IsDataModified
        // unconditionally on a true return, so this is the only place to catch it.
        if (dialog.Value == original)
            return false;

        // Mutates the model's own TagNode in place — that is the contract; the DataNode holds no
        // other reference to the value it displays.
        return TagValues.TryApply(data.Tag, dialog.Value);
    }

    private bool EditByteArray(ByteArrayFormData data)
    {
        byte[] original = data.Data ?? [];

        var dialog = EditValueDialog.Create(
            $"Edit {data.NodeName}",
            $"{original.Length} bytes, hex. The length is fixed by the tag and cannot change here.",
            TagValues.FormatHex(original, data.BytesPerElement),
            validate: text => TagValues.TryParseHex(text, original.Length, out _, out string? error)
                ? null
                : error,
            multiline: true);

        if (ShowDialog<bool>(dialog) != true)
            return false;

        if (!TagValues.TryParseHex(dialog.Value, original.Length, out byte[] parsed, out _))
            return false;

        if (parsed.AsSpan().SequenceEqual(original))
            return false;

        data.Data = parsed;
        return true;
    }

    private bool CreateNode(CreateTagFormData data)
    {
        // Build a throwaway node purely to reuse the icon and colour mapping for the header.
        DataNodeForIcon(data.TagType, out Geometry? icon, out IBrush? accent, out string typeName);

        var dialog = CreateTagDialog.Create(data, icon, accent, typeName);
        return ShowDialog<bool>(dialog) == true;
    }

    private void DataNodeForIcon(TagType type, out Geometry? icon, out IBrush? accent,
                                 out string typeName)
    {
        typeName = ViewModels.NodeViewModel.FriendlyTagType(type);
        icon = null;
        accent = null;

        // A throwaway node of the right class, purely so the icon map can be reused.
        var node = NBTExplorer.Model.TagDataNode.CreateFromTag(TagValues.Default(type));
        if (node is null)
            return;

        icon = Lookup<Geometry>(icons.IconKey(node));
        accent = Lookup<IBrush>(icons.BrushKey(node));
    }

    private T? Lookup<T>(string key) where T : class
        => owner.TryFindResource(key, owner.ActualThemeVariant, out object? value) ? value as T : null;

    private static string DescribeRange(TagType type) => type switch {
        TagType.TAG_BYTE => "Whole number, 0 to 255",
        TagType.TAG_SHORT => "Whole number, −32 768 to 32 767",
        TagType.TAG_INT => "Whole number, −2 147 483 648 to 2 147 483 647",
        TagType.TAG_LONG => "Whole number, 64-bit",
        TagType.TAG_FLOAT => "Number, 32-bit floating point",
        TagType.TAG_DOUBLE => "Number, 64-bit floating point",
        TagType.TAG_STRING => "Text",
        _ => "Value",
    };

    private void ShowDialog(Window dialog) => ShowDialog<object>(dialog);

    /// <summary>
    /// Always pass the owner: Avalonia disables the owner window for the dialog's lifetime, which
    /// is the same protection WinForms modality gave us. Without it, the nested dispatcher frame
    /// would keep delivering input to the main window and the model could be re-entered mid-edit.
    /// </summary>
    private T? ShowDialog<T>(Window dialog)
        => UiThreadBlocking.RunBlocking(() => dialog.ShowDialog<T?>(owner));
}
