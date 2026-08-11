using Avalonia.Controls;

namespace NBTExplorer.Avalonia.Views.Dialogs;

/// <summary>
/// Replaces the MessageBox.Show calls the model makes through FormRegistry.MessageBox, plus the
/// unsaved-changes confirmation that MainForm.ConfirmAction used to raise.
/// </summary>
public partial class MessageDialog : Window
{
    public enum Result { Primary, Secondary, Cancel }

    public MessageDialog() => InitializeComponent();

    public static MessageDialog Info(string message, string title = "NBTExplorer")
        => Build(title, message, ("OK", Result.Primary, true));

    /// <summary>Save / Discard / Cancel, as offered before closing a file with unsaved edits.</summary>
    public static MessageDialog Confirm(string title, string message,
                                        string primary, string secondary)
        => Build(title, message,
                 (primary, Result.Primary, true),
                 (secondary, Result.Secondary, false),
                 ("Cancel", Result.Cancel, false));

    private static MessageDialog Build(string title, string message,
                                       params (string Text, Result Value, bool IsDefault)[] buttons)
    {
        var dialog = new MessageDialog { Title = title };
        dialog.MessageText.Text = message;

        foreach (var (text, value, isDefault) in buttons) {
            var button = new Button {
                Content = text,
                MinWidth = 96,
                IsDefault = isDefault,
                IsCancel = value == Result.Cancel,
            };
            if (isDefault)
                button.Classes.Add("accent");

            var captured = value;
            button.Click += (_, _) => dialog.Close(captured);
            dialog.Buttons.Children.Add(button);
        }

        return dialog;
    }
}
