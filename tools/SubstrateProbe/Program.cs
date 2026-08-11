// Phase 0 probe: does the .NET 2.0 References\Substrate.dll actually work under modern .NET?
//
// Reading is the easy part; the real unknown is the WRITE path (NBTFile.GetDataOutputStream and
// RegionFile's sector allocation), where a subtle behavioural difference silently corrupts saves.
// So every section here writes first and reads back, asserting the value survived.
//
// Usage:
//   SubstrateProbe                       — synthetic round-trips only (no game files needed)
//   SubstrateProbe <path> [<path> ...]   — additionally read real files/dirs (never written to)

using System.Text;
using Substrate.Core;
using Substrate.Nbt;

int failures = 0;
string scratch = Path.Combine(Path.GetTempPath(), "SubstrateProbe_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);

try {
    Section("1. Assembly metadata", () => {
        var asm = typeof(NbtTree).Assembly;
        Log($"name          = {asm.FullName}");
        Log($"imageRuntime  = {asm.ImageRuntimeVersion}");
        Log($"location      = {asm.Location}");
        foreach (var r in asm.GetReferencedAssemblies())
            Log($"references    -> {r.FullName}");
        // Proves the CLR resolved and JIT-ed a Substrate.Nbt method, not just loaded metadata.
        Check(new NbtTree(new TagNodeCompound()).Root != null, "NbtTree instantiates");
    });

    Section("2. GZip NBT round-trip (level.dat shape)", () => {
        string path = Path.Combine(scratch, "level.dat");
        RoundTripNbtFile(path, CompressionType.GZip);
    });

    Section("3. Uncompressed NBT round-trip (idcounts.dat shape)", () => {
        string path = Path.Combine(scratch, "idcounts.dat");
        RoundTripNbtFile(path, CompressionType.None);
    });

    Section("4. Deflate NBT round-trip", () => {
        string path = Path.Combine(scratch, "deflate.nbt");
        RoundTripNbtFile(path, CompressionType.Deflate);
    });

    Section("5. Region file round-trip (.mca — Deflate + sector index)", () => {
        string path = Path.Combine(scratch, "r.0.0.mca");

        // Write a handful of chunks spread across the 32x32 grid, including the corners,
        // so the sector allocator has to grow and reuse the file.
        (int x, int z)[] coords = [(0, 0), (1, 0), (5, 17), (31, 31), (12, 3)];

        var region = new RegionFile(path);
        foreach (var (x, z) in coords) {
            var tree = new NbtTree(MakeChunk(x, z), "");
            using Stream str = region.GetChunkDataOutputStream(x, z);
            tree.WriteTo(str);
        }
        region.Close();

        // Re-open exactly the way RegionFileDataNode.ExpandCore does.
        region = new RegionFile(path);
        int found = 0;
        for (int x = 0; x < 32; x++) {
            for (int z = 0; z < 32; z++) {
                if (!region.HasChunk(x, z))
                    continue;
                found++;
                var tree = new NbtTree();
                tree.ReadFrom(region.GetChunkDataInputStream(x, z));
                Check(tree.Root != null, $"chunk [{x},{z}] has a root");
                Check(tree.Root["xPos"].ToTagInt().Data == x, $"chunk [{x},{z}] xPos survived");
                Check(tree.Root["zPos"].ToTagInt().Data == z, $"chunk [{x},{z}] zPos survived");
            }
        }
        Check(found == coords.Length, $"chunk count round-tripped ({found} of {coords.Length})");

        // Mutate one chunk in place — this is the path RegionChunkDataNode.SaveCore takes,
        // and where a broken sector allocator would corrupt neighbouring chunks.
        {
            var tree = new NbtTree();
            tree.ReadFrom(region.GetChunkDataInputStream(5, 17));
            tree.Root["LastUpdate"] = new TagNodeLong(1234567890123L);
            tree.Root["Sections"] = MakeBigPayload(64 * 1024);   // force the chunk to grow sectors
            using (Stream str = region.GetChunkDataOutputStream(5, 17))
                tree.WriteTo(str);
        }
        region.Close();

        region = new RegionFile(path);
        {
            var tree = new NbtTree();
            tree.ReadFrom(region.GetChunkDataInputStream(5, 17));
            Check(tree.Root["LastUpdate"].ToTagLong().Data == 1234567890123L, "mutated chunk value persisted");
            Check(tree.Root["Sections"].ToTagByteArray().Length == 64 * 1024, "grown chunk payload persisted");
        }
        // Neighbours must be intact after the grown chunk was relocated.
        foreach (var (x, z) in coords) {
            var tree = new NbtTree();
            tree.ReadFrom(region.GetChunkDataInputStream(x, z));
            Check(tree.Root["xPos"].ToTagInt().Data == x, $"neighbour [{x},{z}] intact after regrow");
        }

        region.DeleteChunk(0, 0);
        region.Close();

        region = new RegionFile(path);
        Check(!region.HasChunk(0, 0), "DeleteChunk removed the chunk");
        Check(region.HasChunk(31, 31), "DeleteChunk left other chunks alone");
        region.Close();
    });

    Section("6. Every TagType survives a write/read cycle", () => {
        string path = Path.Combine(scratch, "alltags.nbt");
        var root = new TagNodeCompound {
            ["Byte"] = new TagNodeByte(0x7F),
            ["Short"] = new TagNodeShort(short.MinValue),
            ["Int"] = new TagNodeInt(int.MaxValue),
            ["Long"] = new TagNodeLong(long.MinValue),
            ["Float"] = new TagNodeFloat(3.14159f),
            ["Double"] = new TagNodeDouble(2.718281828459045),
            ["String"] = new TagNodeString("Ünïcödé — строка ✓"),
            ["ByteArray"] = new TagNodeByteArray([1, 2, 3, 250, 255]),
            ["IntArray"] = new TagNodeIntArray([1, -2, int.MaxValue]),
            ["LongArray"] = new TagNodeLongArray([1L, -2L, long.MaxValue]),
        };
        var list = new TagNodeList(TagType.TAG_COMPOUND);
        list.Add(new TagNodeCompound { ["id"] = new TagNodeString("first") });
        list.Add(new TagNodeCompound { ["id"] = new TagNodeString("second") });
        root["List"] = list;
        root["Nested"] = new TagNodeCompound { ["Deep"] = new TagNodeCompound { ["Leaf"] = new TagNodeInt(42) } };

        var file = new NBTFile(path);
        using (Stream str = file.GetDataOutputStream(CompressionType.GZip))
            new NbtTree(root, "AllTags").WriteTo(str);

        var reread = new NbtTree();
        reread.ReadFrom(new NBTFile(path).GetDataInputStream(CompressionType.GZip));
        var r = reread.Root;

        Check(reread.Name == "AllTags", "root name survived");
        Check(r["Byte"].ToTagByte().Data == 0x7F, "TAG_Byte");
        Check(r["Short"].ToTagShort().Data == short.MinValue, "TAG_Short");
        Check(r["Int"].ToTagInt().Data == int.MaxValue, "TAG_Int");
        Check(r["Long"].ToTagLong().Data == long.MinValue, "TAG_Long");
        Check(Math.Abs(r["Float"].ToTagFloat().Data - 3.14159f) < 1e-6, "TAG_Float");
        Check(r["Double"].ToTagDouble().Data == 2.718281828459045, "TAG_Double");
        Check(r["String"].ToTagString().Data == "Ünïcödé — строка ✓", "TAG_String (UTF-8)");
        Check(r["ByteArray"].ToTagByteArray().Data.SequenceEqual(new byte[] { 1, 2, 3, 250, 255 }), "TAG_Byte_Array");
        Check(r["IntArray"].ToTagIntArray().Data.SequenceEqual([1, -2, int.MaxValue]), "TAG_Int_Array");
        Check(r["LongArray"].ToTagLongArray().Data.SequenceEqual([1L, -2L, long.MaxValue]), "TAG_Long_Array");
        Check(r["List"].ToTagList().Count == 2, "TAG_List count");
        Check(r["List"].ToTagList()[1].ToTagCompound()["id"].ToTagString().Data == "second", "TAG_List contents");
        Check(r["Nested"].ToTagCompound()["Deep"].ToTagCompound()["Leaf"].ToTagInt().Data == 42, "nested TAG_Compound");
    });

    Section("7. GZip/None autodetect (NbtFileDataNode.TryCreateFrom behaviour)", () => {
        // TryCreateFrom tries GZip first and falls back to None. That fallback only works if
        // a wrong-compression read fails loudly rather than returning a bogus tree.
        string gz = Path.Combine(scratch, "level.dat");
        string raw = Path.Combine(scratch, "idcounts.dat");
        Check(TryRead(gz, CompressionType.GZip), "gzip file reads as GZip");
        Check(!TryRead(gz, CompressionType.None), "gzip file REJECTS None (fallback ordering holds)");
        Check(TryRead(raw, CompressionType.None), "raw file reads as None");
        Check(!TryRead(raw, CompressionType.GZip), "raw file REJECTS GZip");
    });

    Section("8. Byte-for-byte stability (no-op save must not churn the file)", () => {
        string path = Path.Combine(scratch, "stable.nbt");
        var root = new TagNodeCompound { ["A"] = new TagNodeInt(1), ["B"] = new TagNodeString("two") };
        using (Stream s = new NBTFile(path).GetDataOutputStream(CompressionType.GZip))
            new NbtTree(root, "S").WriteTo(s);
        byte[] first = File.ReadAllBytes(path);

        var tree = new NbtTree();
        tree.ReadFrom(new NBTFile(path).GetDataInputStream(CompressionType.GZip));
        using (Stream s = new NBTFile(path).GetDataOutputStream(CompressionType.GZip))
            tree.WriteTo(s);
        byte[] second = File.ReadAllBytes(path);

        // GZip embeds no timestamp in Substrate's writer, so this should hold. If it doesn't,
        // it is informational, not fatal — the fc /b parity test in verification would need care.
        Report(first.SequenceEqual(second), "read+write reproduces identical bytes",
               fatal: false);
    });

    // ---- optional: real game files supplied on the command line -------------------------------
    if (args.Length > 0) {
        Section("9. Real files", () => {
            foreach (string arg in args) {
                if (Directory.Exists(arg))
                    foreach (string f in Directory.EnumerateFiles(arg, "*", SearchOption.AllDirectories))
                        ProbeRealFile(f);
                else if (File.Exists(arg))
                    ProbeRealFile(arg);
                else
                    Log($"!! not found: {arg}");
            }
        });
    }
    else {
        Log("");
        Log("(no paths given — skipped real-file section; pass a saves\\ folder to exercise it)");
    }
}
finally {
    try { Directory.Delete(scratch, true); } catch { /* best effort */ }
}

Console.WriteLine();
Console.WriteLine(new string('=', 72));
if (failures == 0) {
    Console.WriteLine("RESULT: PASS — Substrate.dll round-trips correctly under .NET "
                      + Environment.Version);
    return 0;
}
Console.WriteLine($"RESULT: FAIL — {failures} check(s) failed under .NET {Environment.Version}");
return 1;

// ---- helpers ---------------------------------------------------------------------------------

void RoundTripNbtFile(string path, CompressionType compression)
{
    var root = new TagNodeCompound {
        ["Data"] = new TagNodeCompound {
            ["LevelName"] = new TagNodeString("Probe World"),
            ["SpawnX"] = new TagNodeInt(128),
            ["SpawnY"] = new TagNodeInt(64),
            ["SpawnZ"] = new TagNodeInt(-256),
            ["RandomSeed"] = new TagNodeLong(-6821066263748282474L),
            ["BorderSize"] = new TagNodeDouble(59999968.0),
        },
    };

    var file = new NBTFile(path);
    using (Stream str = file.GetDataOutputStream(compression))
        new NbtTree(root, "").WriteTo(str);

    long size = new FileInfo(path).Length;
    Check(size > 0, $"wrote {size} bytes with {compression}");

    // Read back exactly the way NbtFileDataNode does.
    var tree = new NbtTree();
    tree.ReadFrom(new NBTFile(path).GetDataInputStream(compression));
    Check(tree.Root != null, "root parsed");
    var data = tree.Root["Data"].ToTagCompound();
    Check(data.Count == 6, $"entry count ({data.Count})");
    Check(data["LevelName"].ToTagString().Data == "Probe World", "string survived");
    Check(data["SpawnZ"].ToTagInt().Data == -256, "negative int survived");
    Check(data["RandomSeed"].ToTagLong().Data == -6821066263748282474L, "64-bit seed survived");

    // Mutate + save + reload — the operation NBTExplorer exists to perform.
    data["SpawnX"] = new TagNodeInt(999);
    data["NewTag"] = new TagNodeString("added");
    using (Stream str = new NBTFile(path).GetDataOutputStream(compression))
        tree.WriteTo(str);

    var after = new NbtTree();
    after.ReadFrom(new NBTFile(path).GetDataInputStream(compression));
    var d2 = after.Root["Data"].ToTagCompound();
    Check(d2["SpawnX"].ToTagInt().Data == 999, "EDIT PERSISTED");
    Check(d2["NewTag"].ToTagString().Data == "added", "ADDED TAG PERSISTED");
    Check(d2["LevelName"].ToTagString().Data == "Probe World", "untouched sibling intact");
}

TagNodeCompound MakeChunk(int x, int z) => new() {
    ["xPos"] = new TagNodeInt(x),
    ["zPos"] = new TagNodeInt(z),
    ["LastUpdate"] = new TagNodeLong(0),
    ["Sections"] = MakeBigPayload(2048),
    ["Entities"] = new TagNodeList(TagType.TAG_COMPOUND),
};

TagNodeByteArray MakeBigPayload(int len)
{
    var bytes = new byte[len];
    for (int i = 0; i < len; i++)
        bytes[i] = (byte)(i * 31);
    return new TagNodeByteArray(bytes);
}

bool TryRead(string path, CompressionType compression)
{
    try {
        var tree = new NbtTree();
        tree.ReadFrom(new NBTFile(path).GetDataInputStream(compression));
        return tree.Root != null;
    }
    catch {
        return false;
    }
}

void ProbeRealFile(string path)
{
    string name = Path.GetFileName(path);

    if (name.StartsWith("r.") && (name.EndsWith(".mca") || name.EndsWith(".mcr"))) {
        try {
            var region = new RegionFile(path);
            int n = 0;
            long tags = 0;
            for (int x = 0; x < 32; x++)
                for (int z = 0; z < 32; z++)
                    if (region.HasChunk(x, z)) {
                        var tree = new NbtTree();
                        tree.ReadFrom(region.GetChunkDataInputStream(x, z));
                        if (tree.Root != null) { n++; tags += tree.Root.Count; }
                    }
            region.Close();
            // An 8192-byte region is header-only. Minecraft legitimately creates these under
            // poi\ and entities\, so "zero chunks" is a valid result, not a Substrate failure.
            long len = new FileInfo(path).Length;
            if (n == 0 && len <= 8192)
                Log($"-- {name}: empty region ({len} bytes, header only)");
            else
                Check(n > 0, $"{name}: read {n} chunks, {tags} root tags");
        }
        catch (Exception ex) {
            Report(false, $"{name}: {ex.GetType().Name}: {ex.Message}");
        }
        return;
    }

    // Same GZip-then-None ordering as NbtFileDataNode.TryCreateFrom.
    foreach (var c in new[] { CompressionType.GZip, CompressionType.None }) {
        try {
            var tree = new NbtTree();
            tree.ReadFrom(new NBTFile(path).GetDataInputStream(c));
            if (tree.Root == null)
                continue;
            Check(true, $"{name}: {c}, root '{tree.Name}', {tree.Root.Count} entries");
            return;
        }
        catch { /* try the next compression */ }
    }
    Log($"-- {name}: not NBT (skipped)");
}

void Section(string title, Action body)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(new string('-', title.Length));
    try {
        body();
    }
    catch (Exception ex) {
        Report(false, $"UNHANDLED {ex.GetType().FullName}: {ex.Message}");
        Log(ex.StackTrace ?? "");
    }
}

void Check(bool ok, string what) => Report(ok, what);

void Report(bool ok, string what, bool fatal = true)
{
    if (ok)
        Console.WriteLine($"  ok    {what}");
    else {
        Console.WriteLine($"  {(fatal ? "FAIL" : "warn")}  {what}");
        if (fatal) failures++;
    }
}

void Log(string s) => Console.WriteLine("  " + s);
