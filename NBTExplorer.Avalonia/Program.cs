using System.Text;
using Avalonia;

namespace NBTExplorer.Avalonia;

internal static class Program
{
    // Ported from NBTExplorer\Program.cs: unhandled exceptions are written to
    // %AppData%\NBTExplorer\error.log rather than vanishing with the process. Saving is
    // destructive and has no undo, so a crash report that says what happened matters.
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NBTExplorer", "error.log");

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogException(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            LogException(e.Exception, "TaskScheduler.UnobservedTaskException");

        App.StartupPaths = args;

        try {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) {
            LogException(ex, "Startup");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()       // cross-platform fallback face
            .LogToTrace();

    private static void LogException(Exception? ex, string source)
    {
        if (ex is null)
            return;

        try {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

            var sb = new StringBuilder()
                .AppendLine(new string('-', 72))
                .AppendLine($"{DateTime.Now:u}  {source}");

            // The original Program.cs specifically reported InnerException — a fix that took a
            // dedicated commit (931ac75) because the outer message alone was useless.
            for (Exception? e = ex; e is not null; e = e.InnerException) {
                sb.AppendLine($"{e.GetType().FullName}: {e.Message}");
                sb.AppendLine(e.StackTrace);
            }

            File.AppendAllText(LogPath, sb.ToString());
        }
        catch {
            // Never let the logger throw out of an exception handler.
        }
    }
}
