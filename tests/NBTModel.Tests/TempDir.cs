namespace NBTModel.Tests;

/// <summary>
/// A scratch directory that deletes itself. Saving in NBTExplorer is destructive and has no undo,
/// so every test that writes works inside one of these and never touches a real world file.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                      "NBTModelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); }
        catch { /* best effort — the OS will reap %TEMP% eventually */ }
    }
}
