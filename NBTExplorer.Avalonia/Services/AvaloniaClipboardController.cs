using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;   // IClipboard
using Avalonia.Threading;
using NBTModel.Interop;

namespace NBTExplorer.Avalonia.Services;

/// <summary>
/// Avalonia implementation of the model's clipboard seam.
///
/// The WinForms controller cannot be ported: it calls Clipboard.SetData with a [Serializable]
/// object, i.e. BinaryFormatter, which is disabled by default in .NET 8 and removed in .NET 9+.
/// Fortunately NbtClipboardData.SerializeNode/DeserializeNode already produce a byte[] via
/// NbtTree, which is fully portable — those bytes just travel under a custom format string.
///
/// Consequence, accepted deliberately: NBT copy/paste does NOT interoperate between the old
/// WinForms app and this one. Different clipboard formats, not worth reconciling.
/// </summary>
public sealed class AvaloniaClipboardController(Window owner) : INbtClipboardController
{
    private const string Format = "application/x-nbtexplorer-tag";

    /// <summary>
    /// NbtClipboardData.SerializeNode encodes the tag but NOT its name, and the model's paste
    /// falls back to "UNNAMED" when the name is missing. So the name travels alongside, in its
    /// own format rather than in DataFormats.Text — that one is a courtesy for other apps and
    /// could contain anything by the time we read it back.
    /// </summary>
    private const string NameFormat = "application/x-nbtexplorer-tag-name";

    /// <summary>
    /// Cached because ContainsData is a SYNCHRONOUS property read on every command-enablement
    /// pass — MainForm.UpdateUI hit it six times per selection change — while Avalonia's
    /// clipboard API is async-only. Bridging it through UiThreadBlocking would pump a nested
    /// dispatcher frame on every arrow-key press.
    /// </summary>
    private bool _hasData;

    private IClipboard? Clipboard => owner.Clipboard;

    public void Initialize()
    {
        NbtClipboardController.Initialize(this);

        // Refresh the cache when the window regains focus, which is when another app could have
        // changed the clipboard behind our back.
        owner.Activated += (_, _) => _ = RefreshAsync();
        _ = RefreshAsync();
    }

    public bool ContainsData => _hasData;

    public void CopyToClipboard(NbtClipboardData data)
    {
        if (Clipboard is null || data.Node is null)
            return;

        var payload = new DataObject();
        payload.Set(Format, NbtClipboardData.SerializeNode(data.Node));
        payload.Set(NameFormat, System.Text.Encoding.UTF8.GetBytes(data.Name ?? ""));
        // Courtesy plain text, so pasting into a text editor shows something meaningful.
        payload.Set(DataFormats.Text, data.Name ?? data.Node.ToString() ?? "");

        _hasData = true;
        _ = Clipboard.SetDataObjectAsync(payload);
    }

    public NbtClipboardData CopyFromClipboard()
    {
        if (Clipboard is null)
            return null!;

        try {
            object? raw = UiThreadBlocking.RunBlocking(() => Clipboard.GetDataAsync(Format));
            if (raw is not byte[] bytes || bytes.Length == 0)
                return null!;

            var node = NbtClipboardData.DeserializeNode(bytes);
            if (node is null)
                return null!;

            object? rawName = UiThreadBlocking.RunBlocking(() => Clipboard.GetDataAsync(NameFormat));
            string? name = rawName is byte[] { Length: > 0 } nameBytes
                ? System.Text.Encoding.UTF8.GetString(nameBytes)
                : null;

            return new NbtClipboardData(name, node);
        }
        catch {
            // The clipboard can hold anything, and another process can take ownership between
            // the check and the read. Failing to paste is recoverable; crashing is not.
            return null!;
        }
    }

    /// <summary>Call after a local copy/cut, and whenever the window is activated.</summary>
    public async Task RefreshAsync()
    {
        if (Clipboard is null)
            return;

        try {
            string[] formats = await Clipboard.GetFormatsAsync();
            bool has = formats.Contains(Format);
            if (has != _hasData) {
                _hasData = has;
                // Command enablement is recomputed from the UI thread.
                Dispatcher.UIThread.Post(() => ClipboardChanged?.Invoke());
            }
        }
        catch {
            // Another process may hold the clipboard open; keep the last known value.
        }
    }

    /// <summary>Raised when the cached availability flag flips, so Paste can re-evaluate.</summary>
    public event Action? ClipboardChanged;
}
