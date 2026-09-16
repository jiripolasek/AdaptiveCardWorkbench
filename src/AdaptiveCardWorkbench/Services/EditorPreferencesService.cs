using Microsoft.UI.Xaml;
using Windows.Storage;

namespace AdaptiveCardWorkbench.Services;

public sealed class EditorPreferencesService
{
    public const double DefaultFontSize = 11;
    public const double MinimumFontSize = 10;
    public const double MaximumFontSize = 24;
    public const string DefaultFontFamily = "Cascadia Mono";

    private const string EditorFontSizeKey = "EditorFontSize";
    private const string EditorFontFamilyKey = "EditorFontFamily";
    private const string AppThemeKey = "AppTheme";

    public EditorPreferencesService()
    {
        object? storedValue = ApplicationData.Current.LocalSettings.Values[EditorFontSizeKey];
        EditorFontSize = storedValue is double fontSize
            ? Math.Clamp(fontSize, MinimumFontSize, MaximumFontSize)
            : DefaultFontSize;

        object? storedFontFamily =
            ApplicationData.Current.LocalSettings.Values[EditorFontFamilyKey];
        EditorFontFamily = storedFontFamily is string fontFamily &&
            !string.IsNullOrWhiteSpace(fontFamily)
                ? fontFamily
                : DefaultFontFamily;

        object? storedTheme = ApplicationData.Current.LocalSettings.Values[AppThemeKey];
        AppTheme = storedTheme is string themeName &&
            Enum.TryParse(themeName, out ElementTheme theme)
                ? theme
                : ElementTheme.Default;
    }

    public double EditorFontSize { get; private set; }

    public string EditorFontFamily { get; private set; }

    public ElementTheme AppTheme { get; private set; }

    public event EventHandler? Changed;

    public void SetEditorFontSize(double fontSize)
    {
        double normalizedFontSize = Math.Clamp(
            Math.Round(fontSize),
            MinimumFontSize,
            MaximumFontSize);
        if (Math.Abs(normalizedFontSize - EditorFontSize) < double.Epsilon)
        {
            return;
        }

        EditorFontSize = normalizedFontSize;
        ApplicationData.Current.LocalSettings.Values[EditorFontSizeKey] =
            normalizedFontSize;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetEditorFontFamily(string fontFamily)
    {
        string normalizedFontFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? DefaultFontFamily
            : fontFamily.Trim();
        if (string.Equals(
            normalizedFontFamily,
            EditorFontFamily,
            StringComparison.Ordinal))
        {
            return;
        }

        EditorFontFamily = normalizedFontFamily;
        ApplicationData.Current.LocalSettings.Values[EditorFontFamilyKey] =
            normalizedFontFamily;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetAppTheme(ElementTheme theme)
    {
        if (theme == AppTheme)
        {
            return;
        }

        AppTheme = theme;
        ApplicationData.Current.LocalSettings.Values[AppThemeKey] =
            theme.ToString();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        SetEditorFontSize(DefaultFontSize);
        SetEditorFontFamily(DefaultFontFamily);
    }
}
