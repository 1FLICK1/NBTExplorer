using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace NBTExplorer.Avalonia.Views.Dialogs;

/// <summary>
/// One dialog covering EditValue, EditString and EditName from the WinForms app. They differed
/// only in their validation rule, so the rule is a delegate rather than three separate windows.
/// </summary>
public partial class EditValueDialog : Window
{
    /// <summary>Returns null when the text is acceptable, or the message to show when it is not.</summary>
    public delegate string? Validator(string text);

    private Validator _validate = _ => null;

    public EditValueDialog()
    {
        InitializeComponent();

        OkButton.Click += OnOk;
        CancelButton.Click += (_, _) => Close(false);
        ValueBox.KeyUp += (_, _) => ErrorText.IsVisible = false;
    }

    public string Value
    {
        get => ValueBox.Text ?? "";
        set => ValueBox.Text = value;
    }

    public static EditValueDialog Create(string title, string prompt, string value,
                                         Validator? validate = null, bool multiline = false)
    {
        var dialog = new EditValueDialog {
            Title = title,
            Value = value,
        };
        dialog.PromptText.Text = prompt;
        dialog._validate = validate ?? (_ => null);

        if (multiline) {
            dialog.ValueBox.AcceptsReturn = true;
            dialog.ValueBox.TextWrapping = TextWrapping.Wrap;
            dialog.ValueBox.MinHeight = 160;
            dialog.ValueBox.MaxHeight = 380;
            // A multi-line box swallows Enter, so the default button would never fire.
            dialog.OkButton.IsDefault = false;
        }

        return dialog;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ValueBox.Focus();
        ValueBox.SelectAll();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        string? error = _validate(Value);
        if (error is not null) {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            ValueBox.Focus();
            return;
        }

        Close(true);
    }
}
