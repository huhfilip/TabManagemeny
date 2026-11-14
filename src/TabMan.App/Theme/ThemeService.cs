using System.Windows;
using TabMan.Core;
using TabMan.Platform.Windows;

namespace TabMan.App.Theme;

public static class ThemeService
{
    public static bool Apply(AppTheme theme)
    {
        var light = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => SystemTheme.PreferLight() ?? false
        };
        var name = light ? "Colors.Light.xaml" : "Colors.Dark.xaml";
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Theme/{name}", UriKind.Absolute)
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(dictionary);
        }
        else
        {
            merged[0] = dictionary;
        }

        return !light;
    }
}
