using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace NBTExplorer.Avalonia.Services;

/// <summary>
/// Runs an async operation to completion while blocking the caller, by pumping a nested
/// dispatcher frame.
///
/// This exists for exactly one reason: <c>FormRegistry</c>'s delegates are synchronous and return
/// <c>bool</c> — <c>delegate bool EditStringAction(StringFormData data)</c> — and callers such as
/// <c>TagDataNode.RenameNode()</c> use the result immediately to decide whether to commit.
/// Avalonia's <c>ShowDialog&lt;T&gt;()</c> returns a Task and does not block. Pumping a frame lets
/// the entire model work unchanged.
///
/// Never use this for anything on a hot path. In particular do NOT use it for
/// <c>INbtClipboardController.ContainsData</c>, which is read on every command-enablement pass —
/// that would pump a frame on each selection change.
///
/// The endgame is to delete this file: NBTModel is to grow value-taking overloads
/// (<c>RenameNode(string)</c>, <c>EditNode(TagNode)</c>, …) so the ViewModels can await dialogs
/// normally. Until then, every call site is one of the five FormRegistry handlers.
/// </summary>
public static class UiThreadBlocking
{
    private static int _depth;

    /// <summary>
    /// Nesting is legitimate — creating a tag can lead to a second dialog — but a handler that
    /// forgets to complete leaves the frame pumping forever and the app looks hung. Anything
    /// past a handful of levels is a bug, so fail loudly in Debug.
    /// </summary>
    private const int MaxDepth = 4;

    public static T RunBlocking<T>(Func<Task<T>> asyncOperation)
    {
        Dispatcher.UIThread.VerifyAccess();

        if (_depth >= MaxDepth)
            throw new InvalidOperationException(
                $"Nested modal depth exceeded {MaxDepth}. A dialog handler is probably not completing.");

        var frame = new DispatcherFrame();
        T result = default!;
        ExceptionDispatchInfo? error = null;

        _depth++;
        try {
            _ = Dispatcher.UIThread.InvokeAsync(async () => {
                try {
                    result = await asyncOperation();
                }
                catch (Exception ex) {
                    error = ExceptionDispatchInfo.Capture(ex);
                }
                finally {
                    frame.Continue = false;
                }
            });

            // Pumps the message loop until Continue is cleared above.
            Dispatcher.UIThread.PushFrame(frame);
        }
        finally {
            _depth--;
        }

        error?.Throw();
        return result;
    }

    /// <summary>True while a nested frame is pumping — the window must refuse to close.</summary>
    public static bool IsModal => _depth > 0;
}
