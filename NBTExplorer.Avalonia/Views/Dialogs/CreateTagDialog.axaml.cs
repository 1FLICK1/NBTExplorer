using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using NBTExplorer.Avalonia.Services;
using NBTModel.Interop;
using Substrate.Nbt;

namespace NBTExplorer.Avalonia.Views.Dialogs;

/// <summary>
/// Replaces Windows\CreateNode.cs (CreateNodeForm). The tag type is fixed by the command that
/// opened the dialog, so this only collects a name and, for scalars, an initial value.
/// </summary>
public partial class CreateTagDialog : Window
{
    private CreateTagFormData _data = new();

    public CreateTagDialog()
    {
        InitializeComponent();
        OkButton.Click += OnOk;
        CancelButton.Click += (_, _) => Close(false);
        NameBox.KeyUp += (_, _) => ErrorText.IsVisible = false;
        ValueBox.KeyUp += (_, _) => ErrorText.IsVisible = false;
    }

    public static CreateTagDialog Create(CreateTagFormData data, Geometry? icon, IBrush? accent,
                                         string typeName)
    {
        var dialog = new CreateTagDialog { _data = data };
        dialog.Title = $"New {typeName} tag";
        dialog.TypeText.Text = typeName;
        dialog.TypeIcon.Data = icon;
        if (accent is not null)
            dialog.TypeIcon.Foreground = accent;

        dialog.NamePanel.IsVisible = data.HasName;

        // Only scalars have a value worth typing. Compounds, lists and arrays start empty and are
        // filled in afterwards.
        bool hasValue = data.TagType is TagType.TAG_BYTE or TagType.TAG_SHORT or TagType.TAG_INT
                                     or TagType.TAG_LONG or TagType.TAG_FLOAT or TagType.TAG_DOUBLE
                                     or TagType.TAG_STRING;
        dialog.ValuePanel.IsVisible = hasValue;
        if (hasValue)
            dialog.ValueBox.Text = TagValues.Format(TagValues.Default(data.TagType));

        return dialog;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (NamePanel.IsVisible) {
            NameBox.Focus();
        }
        else if (ValuePanel.IsVisible) {
            ValueBox.Focus();
            ValueBox.SelectAll();
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        string name = NameBox.Text ?? "";

        if (_data.HasName) {
            if (string.IsNullOrEmpty(name)) {
                Fail("Enter a name.");
                return;
            }
            // A compound cannot hold two tags with the same name; the old CreateNodeForm enforced
            // this through RestrictedNames and so must this.
            if (_data.RestrictedNames.Contains(name)) {
                Fail($"'{name}' is already used in this compound.");
                return;
            }
        }

        TagNode tag = TagValues.Default(_data.TagType);

        if (ValuePanel.IsVisible) {
            string text = ValueBox.Text ?? "";
            if (TagValues.Validate(_data.TagType, text) is { } error) {
                Fail(error);
                return;
            }
            TagValues.TryApply(tag, text);
        }

        _data.TagName = name;
        _data.TagNode = tag;
        Close(true);
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
