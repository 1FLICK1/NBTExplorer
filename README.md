# NBTExplorer

NBTExplorer is an open-source NBT editor for all common sources of NBT data.  It's mainly intended for editing [Minecraft](http://www.minecraft.net) game data.

## Supported Formats

NBTExplorer supports reading and writing the following formats:

* Standard NBT files (e.g. level.dat)
* Schematic files
* Uncompressed NBT files (e.g. idcounts.dat)
* Minecraft region files (*.mcr)
* Minecraft anvil files (*.mca)
* Cubic Chunks region files (r2*.mcr, r2*.mca)

## Two front-ends

The UI is being rewritten on [Avalonia](https://avaloniaui.net/) (Fluent 2 / Windows 11 design
language, one codebase for Windows, Linux and macOS). Both front-ends sit on the same UI-free
`NBTModel` layer.

| | `legacy/NBTExplorer` (WinForms) | `NBTExplorer.Avalonia` |
|---|---|---|
| Runtime | .NET Framework 2.0 | .NET 10 |
| Solution | `legacy/NBTExplorer.sln` | `NBTExplorer.Avalonia.slnx` |
| Status | Reference implementation, frozen | Under construction |
| Browsing | yes | yes |
| Editing | yes | yes |
| Search | yes | not yet |

The WinForms tree lives under `legacy/` and is the behavioural reference. Building it needs Visual
Studio and the .NET Framework 3.5 targeting pack; without those it will not build (`MSB3645`).

`NBTModel` stays at the top level because both front-ends build against it.

## Building the Avalonia app

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) (the exact version is pinned in
`global.json`).

```bash
dotnet run --project NBTExplorer.Avalonia
```

Paths can be passed on the command line, so the executable also works as a shell "Open with"
target:

```bash
dotnet run --project NBTExplorer.Avalonia -- "%APPDATA%\.minecraft\saves\My World"
```

Tests:

```bash
dotnet test NBTExplorer.Avalonia.slnx
```

`tools/SubstrateProbe` verifies that the prebuilt .NET 2.0 `References/Substrate.dll` still reads
and writes every supported format correctly under modern .NET. Run it with no arguments for
synthetic round-trips, or point it at a saves folder:

```bash
dotnet run --project tools/SubstrateProbe -- "%APPDATA%\.minecraft\saves"
```

## System Requirements

### Avalonia version

.NET 10 runtime. Windows 10+, Linux (X11/Wayland) or macOS.

### WinForms version

Windows XP or later, .NET Framework 2.0 or later.

Under Linux it runs on recent Mono runtimes, at least 2.6 or later; minimally you need the
`mono-core` and `mono-winforms` packages, or whatever package set is equivalent.

A separate Mac version with a native UI once existed (MonoMac), but it had stopped building — it
referenced source files that had since moved into `NBTModel` — and has been removed. The Avalonia
app replaces it.
