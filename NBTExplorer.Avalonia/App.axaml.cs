using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NBTExplorer.Avalonia.Services;
using NBTExplorer.Avalonia.ViewModels;
using NBTExplorer.Avalonia.Views;

namespace NBTExplorer.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Paths passed on the command line, so the app works as a shell "Open with" target and as a
    /// drop target on the executable itself.
    /// </summary>
    public static string[] StartupPaths { get; set; } = [];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = new ServiceCollection()
            .AddSingleton<IIconMap, IconMap>()
            .AddSingleton<ExplorerViewModel>()
            .AddSingleton<MainWindowViewModel>()
            .BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            // Avalonia's built-in data annotation validator duplicates every validation error
            // when CommunityToolkit.Mvvm is also in play; the standard fix is to remove it.
            DisableAvaloniaDataAnnotationValidation();

            desktop.MainWindow = new MainWindow {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };

            var startup = StartupPaths
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToList();
            if (startup.Count > 0)
                Services.GetRequiredService<ExplorerViewModel>().OpenPaths(startup);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var toRemove = BindingPlugins.DataValidators
            .OfType<DataAnnotationsValidationPlugin>()
            .ToArray();

        foreach (var plugin in toRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
