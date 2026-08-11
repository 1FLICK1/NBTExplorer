using NBTExplorer.Avalonia.Services;
using NBTExplorer.Avalonia.ViewModels;
using Substrate.Core;
using Substrate.Nbt;
using Xunit;

namespace NBTExplorer.Avalonia.Tests;

/// <summary>
/// The breadcrumb and window title render <see cref="NodeViewModel.Name"/>. A nested compound
/// showed up as "51 entries" instead of its tag name, so this pins the naming of every level.
/// </summary>
public class BreadcrumbNameTests
{
    private static (ExplorerViewModel vm, string dir) OpenNested()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NBTCrumb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "level.dat");

        var root = new TagNodeCompound {
            ["Data"] = new TagNodeCompound {
                ["GameRules"] = new TagNodeCompound {
                    ["doFireTick"] = new TagNodeString("true"),
                    ["doMobLoot"] = new TagNodeString("true"),
                },
                ["SpawnX"] = new TagNodeInt(1),
            },
        };
        using (Stream s = new NBTFile(path).GetDataOutputStream(CompressionType.GZip))
            new NbtTree(root, "").WriteTo(s);

        var vm = new ExplorerViewModel(new IconMap());
        vm.OpenPaths([path]);
        return (vm, dir);
    }

    [Fact]
    public void NestedCompoundKeepsItsTagNameInTheBreadcrumb()
    {
        var (vm, dir) = OpenNested();
        try {
            vm.Navigate(vm.Roots.Single());
            vm.Navigate(vm.Items.Single(i => i.Name == "Data"));
            var rules = vm.Items.Single(i => i.Name == "GameRules");
            vm.Navigate(rules);

            Assert.Equal("GameRules", vm.CurrentFolder!.Name);
            Assert.Equal(["level.dat", "Data", "GameRules"], vm.Breadcrumb.Select(n => n.Name));
        }
        finally {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// Runs against a real Minecraft level.dat when one has been copied into testdata\, which is
    /// where the "51 entries" breadcrumb was first seen. Skipped when the file is absent so the
    /// suite still passes on a clean checkout.
    /// </summary>
    [Fact]
    public void RealWorldFileNamesEveryLevelOfTheBreadcrumb()
    {
        string? path = FindTestData("level.dat");
        if (path is null)
            return;   // no sample file on this machine; nothing to assert

        var vm = new ExplorerViewModel(new IconMap());
        vm.OpenPaths([path!]);

        var file = vm.Roots.Single();
        vm.Navigate(file);
        var data = vm.Items.Single(i => i.Name == "Data");
        vm.Navigate(data);

        // Walk into every container child and assert none of them lose their name.
        foreach (var child in vm.Items.Where(i => i.IsContainer).ToList()) {
            string expected = child.Name;
            vm.Navigate(child);

            Assert.Equal(expected, vm.CurrentFolder!.Name);
            Assert.DoesNotContain("entries", vm.CurrentFolder.Name);
            Assert.Equal(["level.dat", "Data", expected], vm.Breadcrumb.Select(n => n.Name));

            vm.Navigate(data);
        }
    }

    /// <summary>
    /// Collapsing an ancestor in the nav pane calls DataNode.Release, which clears the child
    /// collection and sets every child's Parent to null. A stale CurrentFolder pointing into that
    /// released subtree loses its NodeName and renders as "51 entries" — the tag's display text
    /// with an empty name prefix. The current folder has to follow the collapse instead.
    /// </summary>
    [Fact]
    public void CollapsingAnAncestorDoesNotStrandTheCurrentFolder()
    {
        var (vm, dir) = OpenNested();
        try {
            var file = vm.Roots.Single();
            vm.Navigate(file);
            var data = vm.Items.Single(i => i.Name == "Data");
            vm.Navigate(data);
            var rules = vm.Items.Single(i => i.Name == "GameRules");
            vm.Navigate(rules);
            Assert.Equal("GameRules", vm.CurrentFolder!.Name);

            // The user collapses "level.dat" in the nav pane while sitting inside GameRules.
            file.IsExpanded = false;

            Assert.DoesNotContain("entries", vm.CurrentFolder!.Name);
            Assert.All(vm.Breadcrumb, n => Assert.DoesNotContain("entries", n.Name));
        }
        finally {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static string? FindTestData(string name)
    {
        if (IconMapTests.FindRepoRoot() is not { } root)
            return null;

        string candidate = Path.Combine(root, "testdata", name);
        return File.Exists(candidate) ? candidate : null;
    }

    [Fact]
    public void NavigatingViaTheNavTreeGivesTheSameNames()
    {
        var (vm, dir) = OpenNested();
        try {
            // The nav pane hands over ViewModels straight from the Children collection, without
            // the details list ever having materialised them.
            var file = vm.Roots.Single();
            file.EnsureExpanded();
            var data = file.Children.Single(c => c.Name == "Data");
            data.EnsureExpanded();
            var rules = data.Children.Single(c => c.Name == "GameRules");

            vm.Navigate(rules);

            Assert.Equal("GameRules", vm.CurrentFolder!.Name);
            Assert.Equal(["level.dat", "Data", "GameRules"], vm.Breadcrumb.Select(n => n.Name));
        }
        finally {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
