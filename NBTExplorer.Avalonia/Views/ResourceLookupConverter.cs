using System.Globalization;
using Avalonia;
using Avalonia.Controls;   // ResourceNodeExtensions.TryFindResource
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace NBTExplorer.Avalonia.Views;

/// <summary>
/// Resolves a resource key produced by <see cref="Services.IconMap"/> into the actual resource.
///
/// The ViewModel deliberately yields a string key rather than a Geometry or IBrush: keeping
/// Avalonia types out of the ViewModels means the icon mapping stays a plain dictionary that can
/// be unit-tested without a UI thread, and theme changes resolve through the live resource system
/// rather than being baked in at bind time.
/// </summary>
public sealed class ResourceLookupConverter : IValueConverter
{
    public static readonly ResourceLookupConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is null)
            return null;

        ThemeVariant variant = Application.Current.ActualThemeVariant;
        if (!Application.Current.TryFindResource(key, variant, out object? resource))
            return null;

        // Guard against a key that resolves to the wrong kind of resource — a typo in IconMap
        // would otherwise surface as a silent blank icon.
        return resource switch {
            Geometry g when targetType.IsAssignableFrom(typeof(Geometry)) => g,
            IBrush b when targetType.IsAssignableFrom(typeof(IBrush)) => b,
            _ => resource,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Renders a TagType as the readable name used in the New menu.</summary>
public sealed class TagTypeNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Substrate.Nbt.TagType type
            ? ViewModels.NodeViewModel.FriendlyTagType(type)
            : value?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
