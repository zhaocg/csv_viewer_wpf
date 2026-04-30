using System.Windows;
using System.Windows.Media;

namespace CsvViewer.Services;

public static class ThemeService
{
    public const string LightTheme = "亮色";
    public const string DarkTheme = "暗色";

    public static void Apply(string? theme)
    {
        var isDark = string.Equals(theme, DarkTheme, StringComparison.OrdinalIgnoreCase);
        if (isDark)
        {
            ApplyDarkTheme();
            return;
        }

        ApplyLightTheme();
    }

    private static void ApplyLightTheme()
    {
        SetBrush("AppBackgroundBrush", "#F3F6FB");
        SetBrush("SurfaceBrush", "#FFFFFF");
        SetBrush("SurfaceElevatedBrush", "#FBFCFF");
        SetBrush("SurfaceMutedBrush", "#EEF3FA");
        SetBrush("PanelDarkBrush", "#0F172A");
        SetBrush("PanelDarkMutedBrush", "#182236");
        SetBrush("AppBorderBrush", "#D7DFEA");
        SetBrush("GridLineBrush", "#E5EAF2");
        SetBrush("TextPrimaryBrush", "#111827");
        SetBrush("TextSecondaryBrush", "#526071");
        SetBrush("TextMutedBrush", "#7B8797");
        SetBrush("AccentBrush", "#2563EB");
        SetBrush("AccentHoverBrush", "#1D4ED8");
        SetBrush("AccentSoftBrush", "#DBEAFE");
        SetBrush("AccentCyanBrush", "#0891B2");
        SetBrush("MenuTextBrush", "#111827");
        SetBrush("MenuDisabledTextBrush", "#9CA3AF");
        SetBrush("DataGridAlternatingRowBrush", "#FAFCFF");
        SetBrush("DataGridHeaderBrush", "#EEF3FA");
        SetBrush("DataGridRowHeaderBrush", "#F6F8FC");
        SetBrush("LoadingOverlayBrush", "#C8F8FAFC");
        SetBrush("DocumentTabBarBrush", "#E8EEF7");
        SetBrush("DocumentTabBackgroundBrush", "#EEF3FA");
        SetBrush("DocumentTabHoverBrush", "#E1E8F2");
        SetBrush("DocumentTabSelectedBrush", "#FFFFFF");
        SetBrush("DocumentTabTextBrush", "#526071");
        SetBrush("DocumentTabSelectedTextBrush", "#111827");
        SetTopBar("#101827", "#1D2B4A", "#1E3A8A");
    }

    private static void ApplyDarkTheme()
    {
        SetBrush("AppBackgroundBrush", "#1E1E1E");
        SetBrush("SurfaceBrush", "#1E1E1E");
        SetBrush("SurfaceElevatedBrush", "#252526");
        SetBrush("SurfaceMutedBrush", "#252526");
        SetBrush("PanelDarkBrush", "#181818");
        SetBrush("PanelDarkMutedBrush", "#252526");
        SetBrush("AppBorderBrush", "#3C3C3C");
        SetBrush("GridLineBrush", "#333333");
        SetBrush("TextPrimaryBrush", "#D4D4D4");
        SetBrush("TextSecondaryBrush", "#CCCCCC");
        SetBrush("TextMutedBrush", "#8E8E8E");
        SetBrush("AccentBrush", "#007ACC");
        SetBrush("AccentHoverBrush", "#1177BB");
        SetBrush("AccentSoftBrush", "#264F78");
        SetBrush("AccentCyanBrush", "#4FC1FF");
        SetBrush("MenuTextBrush", "#CCCCCC");
        SetBrush("MenuDisabledTextBrush", "#6A6A6A");
        SetBrush("DataGridAlternatingRowBrush", "#202020");
        SetBrush("DataGridHeaderBrush", "#252526");
        SetBrush("DataGridRowHeaderBrush", "#252526");
        SetBrush("LoadingOverlayBrush", "#B0181818");
        SetBrush("DocumentTabBarBrush", "#0F0F0F");
        SetBrush("DocumentTabBackgroundBrush", "#151515");
        SetBrush("DocumentTabHoverBrush", "#1B1B1B");
        SetBrush("DocumentTabSelectedBrush", "#2D2D30");
        SetBrush("DocumentTabTextBrush", "#8A8F98");
        SetBrush("DocumentTabSelectedTextBrush", "#FFFFFF");
        SetTopBar("#181818", "#1F1F1F", "#252526");
    }

    private static void SetBrush(string key, string color)
    {
        var newColor = (Color)ColorConverter.ConvertFromString(color);
        if (Application.Current.Resources[key] is SolidColorBrush brush)
        {
            if (brush.IsFrozen)
            {
                Application.Current.Resources[key] = new SolidColorBrush(newColor);
                return;
            }

            brush.Color = newColor;
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(newColor);
    }

    private static void SetTopBar(string start, string middle, string end)
    {
        var startColor = (Color)ColorConverter.ConvertFromString(start);
        var middleColor = (Color)ColorConverter.ConvertFromString(middle);
        var endColor = (Color)ColorConverter.ConvertFromString(end);

        if (Application.Current.Resources["TopBarBrush"] is not LinearGradientBrush brush || brush.GradientStops.Count < 3 || brush.IsFrozen)
        {
            Application.Current.Resources["TopBarBrush"] = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop(startColor, 0),
                    new GradientStop(middleColor, 0.65),
                    new GradientStop(endColor, 1)
                ]
            };
            return;
        }

        brush.GradientStops[0].Color = startColor;
        brush.GradientStops[1].Color = middleColor;
        brush.GradientStops[2].Color = endColor;
    }
}
